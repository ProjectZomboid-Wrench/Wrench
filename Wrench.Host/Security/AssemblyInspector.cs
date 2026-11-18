using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Wrench.Host.Security;

/// <summary>
/// Checks a mod assembly's metadata and IL against <see cref="ModPolicy"/> without loading it. References into the
/// mod's own assemblies are allowed (those are inspected too); everything else is framework and must be allowed.
/// </summary>
internal sealed class AssemblyInspector(IReadOnlySet<string> modAssemblyNames, IReadOnlySet<string> frameworkAssemblyNames)
{
    // The API a mod is meant to use: everything in it goes through Kahlua, as Lua's access does.
    private static readonly HashSet<string> WrenchAssemblies = ["Wrench.Api", "Wrench.Game.Java", "Wrench.Game.Lua"];

    private const string BinderType = "Microsoft.CSharp.RuntimeBinder.Binder";
    private const string TypeType = "System.Type";

    public List<string> Inspect(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
            return ["not a .NET assembly"];
        var md = pe.GetMetadataReader();
        var violations = new List<string>();
        CheckIdentity(md, Path.GetFileNameWithoutExtension(path), violations);
        CheckAssemblyReferences(md, violations);
        CheckTypeDefinitions(md, violations);
        CheckTypeReferences(md, violations);
        CheckMemberReferences(md, violations);
        CheckNativeCode(md, violations);
        CheckForwarders(md, violations);
        CheckMethodBodies(pe, md, violations);
        return violations;
    }

    // The load context resolves a mod's assemblies by file name, so what was vetted must be what's asked for.
    private static void CheckIdentity(MetadataReader md, string fileName, List<string> violations)
    {
        if (!md.IsAssembly)
            violations.Add("is a module, not an assembly");
        else if (md.GetString(md.GetAssemblyDefinition().Name) is var name && !string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            violations.Add($"is assembly {name} in a file named {fileName}.dll");
        if (md.GetTableRowCount(TableIndex.File) > 0 || md.GetTableRowCount(TableIndex.ModuleRef) > 0)
            violations.Add("references other modules or native libraries");
    }

    // Anything else would be resolved by the load context from somewhere vetting never looked.
    private void CheckAssemblyReferences(MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.AssemblyReferences)
        {
            var name = md.GetString(md.GetAssemblyReference(handle).Name);
            if (!modAssemblyNames.Contains(name) && !WrenchAssemblies.Contains(name) && !frameworkAssemblyNames.Contains(name))
                violations.Add($"references assembly {name}, which isn't framework, Wrench API or shipped with the mod");
        }
    }

    /// <summary>
    /// The runtime recognises some attributes by name wherever they're defined: a mod's own
    /// <c>IgnoresAccessChecksToAttribute</c> or <c>UnsafeAccessorAttribute</c> would get past access checks. Mods may
    /// define types in framework namespaces only for attributes the compiler embeds. Explicit layout can overlap
    /// object references, which is type confusion the verifier doesn't see.
    /// </summary>
    private static void CheckTypeDefinitions(MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            var name = md.GetString(type.Name);
            var ns = type.IsNested ? "" : md.GetString(type.Namespace);
            if (IsFrameworkNamespace(ns) && !CompilerEmbeddedTypes.Contains($"{ns}.{name}"))
                violations.Add($"defines {ns}.{name} in a framework namespace");
            if ((type.Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout)
                violations.Add($"defines {TypeName(md, handle)} with explicit layout");
        }
    }

    private static bool IsFrameworkNamespace(string ns) =>
        ns is "System" or "Microsoft" || ns.StartsWith("System.", StringComparison.Ordinal)
        || ns.StartsWith("Microsoft.", StringComparison.Ordinal);

    private static readonly HashSet<string> CompilerEmbeddedTypes =
    [
        "Microsoft.CodeAnalysis.EmbeddedAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute",
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
        "System.Runtime.CompilerServices.RequiresLocationAttribute",
        "System.Runtime.CompilerServices.ExtensionMarkerAttribute",
    ];

    private void CheckTypeReferences(MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.TypeReferences)
        {
            // A nil scope is looked up in the exported type table; its handle kind reads as ModuleDefinition.
            if (OutermostScope(md, handle) is var scope && (scope.IsNil || scope.Kind is not (HandleKind.AssemblyReference or HandleKind.ModuleDefinition)))
            {
                violations.Add($"references type {md.GetString(md.GetTypeReference(handle).Name)} through the exported type table or another module");
                continue;
            }
            if (!IsFramework(md, handle, out var ns, out var fullName))
                continue;
            if (!ModPolicy.IsTypeAllowed(ns, fullName) && fullName != TypeType && fullName != "System.RuntimeTypeHandle")
                violations.Add($"uses {fullName}");
        }
    }

    private void CheckMemberReferences(MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.MemberReferences)
        {
            var member = md.GetMemberReference(handle);
            var name = md.GetString(member.Name);
            var parent = ParentTypeName(md, member.Parent);
            if (parent is null)
            {
                // Arrays, generic parameters and the like: nothing to look up by type, but denied names still are.
                if (member.Parent.Kind == HandleKind.TypeSpecification && !IsGenericInstance(md, member.Parent)
                    && ModPolicy.DeniedMembers.Contains(name))
                    violations.Add($"uses {name}");
                continue;
            }

            if (ModPolicy.DeniedMembers.Contains(name))
                violations.Add($"uses {parent}::{name}");
            else if (ModPolicy.RestrictedTypes.TryGetValue(parent, out var allowed) && !allowed.Contains(name))
                violations.Add($"uses {parent}::{name}");
        }
    }

    private static void CheckNativeCode(MetadataReader md, List<string> violations)
    {
        if (md.GetTableRowCount(TableIndex.ImplMap) > 0)
            violations.Add("declares P/Invoke (native) methods");

        foreach (var handle in md.MethodDefinitions)
        {
            var method = md.GetMethodDefinition(handle);
            var impl = method.ImplAttributes;
            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0 || (impl & MethodImplAttributes.InternalCall) != 0)
                violations.Add($"declares native method {md.GetString(method.Name)}");
            else if ((impl & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.Runtime)
            {
                if (!IsDelegateType(md, method.GetDeclaringType()))
                    violations.Add($"declares runtime-implemented method {md.GetString(method.Name)}");
            }
            // Bodiless non-abstract methods are extern: [UnsafeAccessor] and the like, implemented by the runtime.
            else if (method.RelativeVirtualAddress == 0 && (method.Attributes & MethodAttributes.Abstract) == 0)
                violations.Add($"declares extern method {md.GetString(method.Name)}");
        }
    }

    private static void CheckForwarders(MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.ExportedTypes)
        {
            var exported = md.GetExportedType(handle);
            var verb = exported.IsForwarder ? "forwards" : "exports";
            violations.Add($"{verb} type {md.GetString(exported.Namespace)}.{md.GetString(exported.Name)}");
        }
    }

    /// <summary>
    /// A <c>dynamic</c> call site names its member as a string given to the runtime binder, so nothing in the metadata
    /// shows what it will reach. Each binder call that takes a member name must get it from a string literal in the
    /// same straight-line code, and that literal is checked against <see cref="ModPolicy.DeniedDynamicMembers"/>.
    /// </summary>
    private void CheckMethodBodies(PEReader pe, MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.MethodDefinitions)
        {
            var method = md.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            var (instructions, targets) = Decode(body.GetILBytes()!);
            foreach (var region in body.ExceptionRegions)
            {
                targets.Add(region.TryOffset);
                targets.Add(region.HandlerOffset);
                if (region.Kind == ExceptionRegionKind.Filter)
                    targets.Add(region.FilterOffset);
            }

            var name = $"{TypeName(md, method.GetDeclaringType())}::{md.GetString(method.Name)}";
            for (var i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                if (!IsMethodToken(instruction.OpCode) || BinderMember(md, instruction) is not { } member)
                    continue;
                if (instruction.OpCode != ILOpCode.Call || !BinderMethods.TryGetValue(member.Name, out var namesMember))
                {
                    violations.Add($"{name} uses {BinderType}::{member.Name}");
                    continue;
                }
                if (!namesMember)
                    continue;

                var value = ConstantArgument(md, instructions, targets, i, member.ParameterCount - 2);
                if (value is null)
                    violations.Add($"{name} gives the dynamic binder a member name that isn't a string literal");
                else if (ModPolicy.DeniedDynamicMembers.Contains(value))
                    violations.Add($"{name} uses dynamic member {value}");
            }
        }
    }

    // Binder factory methods, and whether their second parameter is a member name.
    private static readonly Dictionary<string, bool> BinderMethods = new()
    {
        ["GetMember"] = true, ["SetMember"] = true, ["InvokeMember"] = true, ["IsEvent"] = true,
        ["Invoke"] = false, ["InvokeConstructor"] = false, ["GetIndex"] = false, ["SetIndex"] = false,
        ["Convert"] = false, ["UnaryOperation"] = false, ["BinaryOperation"] = false,
    };

    private static bool IsMethodToken(ILOpCode opCode) =>
        opCode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Jmp;

    private (string Name, int ParameterCount)? BinderMember(MetadataReader md, Instruction instruction)
    {
        var handle = MetadataTokens.EntityHandle(instruction.Operand);
        if (handle.Kind == HandleKind.MethodSpecification)
            handle = md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if (handle.Kind != HandleKind.MemberReference)
            return null;
        var member = md.GetMemberReference((MemberReferenceHandle)handle);
        if (ParentTypeName(md, member.Parent) != BinderType)
            return null;
        return (md.GetString(member.Name), CallSignature(md, member).Parameters);
    }

    private static (int Parameters, bool HasThis, bool ReturnsValue) CallSignature(MetadataReader md, MemberReference member)
    {
        var blob = md.GetBlobReader(member.Signature);
        var header = blob.ReadSignatureHeader();
        if (header.IsGeneric)
            blob.ReadCompressedInteger();
        var parameters = blob.ReadCompressedInteger();
        var returnType = blob.ReadSignatureTypeCode();
        return (parameters, header.IsInstance, returnType != SignatureTypeCode.Void);
    }

    /// <summary>
    /// Walks back from the call at <paramref name="call"/> to the instruction that pushed the argument
    /// <paramref name="depth"/> slots below the top of the stack, and returns it if it's a <c>ldstr</c>. Gives up
    /// (returns null) on anything it can't account for, including control flow joining in between.
    /// </summary>
    private static string? ConstantArgument(MetadataReader md, List<Instruction> instructions, HashSet<int> targets,
        int call, int depth)
    {
        if (targets.Contains(instructions[call].Offset))
            return null;
        for (var i = call - 1; i >= 0; i--)
        {
            var instruction = instructions[i];
            if (StackEffect(md, instruction) is not var (pops, pushes))
                return null;
            if (depth < pushes)
            {
                if (instruction.OpCode == ILOpCode.Dup)
                    depth = 0;
                else
                    return instruction.OpCode == ILOpCode.Ldstr
                        ? md.GetUserString(MetadataTokens.UserStringHandle(instruction.Operand))
                        : null;
            }
            else
                depth += pops - pushes;
            if (targets.Contains(instruction.Offset))
                return null;
        }
        return null;
    }

    private static (int Pops, int Pushes)? StackEffect(MetadataReader md, Instruction instruction)
    {
        switch (instruction.OpCode)
        {
            case ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3
                or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8
                or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 or ILOpCode.Ldstr or ILOpCode.Ldnull or ILOpCode.Ldtoken
                or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s
                or ILOpCode.Ldloc or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3
                or ILOpCode.Ldarg_s or ILOpCode.Ldarg or ILOpCode.Ldsfld:
                return (0, 1);
            case ILOpCode.Dup:
                return (1, 2);
            case ILOpCode.Newarr:
                return (1, 1);
            case ILOpCode.Stelem_ref:
                return (3, 0);
            case ILOpCode.Call:
                var handle = MetadataTokens.EntityHandle(instruction.Operand);
                if (handle.Kind != HandleKind.MemberReference)
                    return null;
                var (parameters, hasThis, returnsValue) = CallSignature(md, md.GetMemberReference((MemberReferenceHandle)handle));
                return (parameters + (hasThis ? 1 : 0), returnsValue ? 1 : 0);
            default:
                return null;
        }
    }

    private bool IsFramework(MetadataReader md, TypeReferenceHandle handle, out string ns, out string fullName)
    {
        var outermost = handle;
        var names = new List<string>();
        while (true)
        {
            var reference = md.GetTypeReference(outermost);
            names.Insert(0, md.GetString(reference.Name));
            if (reference.ResolutionScope.Kind != HandleKind.TypeReference)
                break;
            outermost = (TypeReferenceHandle)reference.ResolutionScope;
        }

        var top = md.GetTypeReference(outermost);
        ns = md.GetString(top.Namespace);
        fullName = (ns.Length > 0 ? ns + "." : "") + string.Join("/", names);
        if (top.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return false;
        var assembly = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)top.ResolutionScope).Name);
        return !modAssemblyNames.Contains(assembly) && !WrenchAssemblies.Contains(assembly);
    }

    private string? ParentTypeName(MetadataReader md, EntityHandle parent)
    {
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return IsFramework(md, (TypeReferenceHandle)parent, out _, out var fullName) ? fullName : null;
            case HandleKind.TypeSpecification:
                var blob = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                    return null;
                blob.ReadSignatureTypeCode();
                var generic = blob.ReadTypeHandle();
                return generic.Kind == HandleKind.TypeReference ? ParentTypeName(md, generic) : null;
            default:
                return null;
        }
    }

    private static EntityHandle OutermostScope(MetadataReader md, TypeReferenceHandle handle)
    {
        var scope = md.GetTypeReference(handle).ResolutionScope;
        while (scope.Kind == HandleKind.TypeReference)
            scope = md.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
        return scope;
    }

    private static bool IsGenericInstance(MetadataReader md, EntityHandle typeSpec) =>
        md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)typeSpec).Signature).ReadSignatureTypeCode()
        == SignatureTypeCode.GenericTypeInstance;

    private static bool IsDelegateType(MetadataReader md, TypeDefinitionHandle handle)
    {
        var baseType = md.GetTypeDefinition(handle).BaseType;
        if (baseType.Kind != HandleKind.TypeReference)
            return false;
        var reference = md.GetTypeReference((TypeReferenceHandle)baseType);
        return md.GetString(reference.Namespace) == "System" && md.GetString(reference.Name) == "MulticastDelegate";
    }

    private static string TypeName(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var ns = md.GetString(type.Namespace);
        return (ns.Length > 0 ? ns + "." : "") + md.GetString(type.Name);
    }

    private readonly record struct Instruction(int Offset, ILOpCode OpCode, int Operand);

    private static readonly ImmutableDictionary<ushort, OperandType> OperandTypes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToImmutableDictionary(o => unchecked((ushort)o.Value), o => o.OperandType);

    private static (List<Instruction> Instructions, HashSet<int> BranchTargets) Decode(byte[] il)
    {
        var instructions = new List<Instruction>();
        var targets = new HashSet<int>();
        var offset = 0;
        while (offset < il.Length)
        {
            var start = offset;
            ushort value = il[offset++];
            if (value == 0xFE)
                value = (ushort)(0xFE00 | il[offset++]);
            var operandType = OperandTypes[value];
            var operand = 0;
            switch (operandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                    operand = (sbyte)il[offset];
                    offset += 1;
                    targets.Add(offset + operand);
                    break;
                case OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    operand = il[offset];
                    offset += 1;
                    break;
                case OperandType.InlineBrTarget:
                    operand = BitConverter.ToInt32(il, offset);
                    offset += 4;
                    targets.Add(offset + operand);
                    break;
                case OperandType.InlineVar:
                    offset += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    offset += 8;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, offset);
                    var next = offset + 4 + count * 4;
                    for (var k = 0; k < count; k++)
                        targets.Add(next + BitConverter.ToInt32(il, offset + 4 + k * 4));
                    offset = next;
                    break;
                default:
                    operand = BitConverter.ToInt32(il, offset);
                    offset += 4;
                    break;
            }
            instructions.Add(new Instruction(start, (ILOpCode)value, operand));
        }
        return (instructions, targets);
    }
}
