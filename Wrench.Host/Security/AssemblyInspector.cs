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
internal sealed class AssemblyInspector(IReadOnlySet<string> modAssemblyNames)
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
        CheckTypeReferences(md, violations);
        CheckMemberReferences(md, violations);
        CheckNativeCode(md, violations);
        CheckForwarders(md, violations);
        CheckMethodBodies(pe, md, violations);
        return violations;
    }

    private void CheckTypeReferences(MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.TypeReferences)
        {
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
                continue;

            if (ModPolicy.DeniedMembers.Contains(name))
                violations.Add($"uses {parent}::{name}");
            else if (parent == TypeType && name != "GetTypeFromHandle")
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
            else if ((impl & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.Runtime && !IsDelegateType(md, method.GetDeclaringType()))
                violations.Add($"declares runtime-implemented method {md.GetString(method.Name)}");
        }
    }

    private static void CheckForwarders(MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.ExportedTypes)
        {
            var exported = md.GetExportedType(handle);
            if (exported.IsForwarder)
                violations.Add($"forwards type {md.GetString(exported.Namespace)}.{md.GetString(exported.Name)}");
        }
    }

    /// <summary>
    /// A <c>dynamic</c> call site names its member as a string given to the runtime binder, so nothing in the metadata
    /// shows what it will reach. Every string literal in a method that calls the binder is treated as a possible
    /// member name and checked against <see cref="ModPolicy.DeniedDynamicMembers"/>.
    /// </summary>
    private void CheckMethodBodies(PEReader pe, MetadataReader md, List<string> violations)
    {
        foreach (var handle in md.MethodDefinitions)
        {
            var method = md.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            var instructions = Decode(pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!);
            if (!instructions.Any(i => i.OpCode is ILOpCode.Call or ILOpCode.Callvirt && ParentOfCall(md, i) == BinderType))
                continue;

            var name = $"{TypeName(md, method.GetDeclaringType())}::{md.GetString(method.Name)}";
            foreach (var instruction in instructions.Where(i => i.OpCode == ILOpCode.Ldstr))
            {
                var value = md.GetUserString(MetadataTokens.UserStringHandle(instruction.Operand));
                if (ModPolicy.DeniedDynamicMembers.Contains(value))
                    violations.Add($"{name} uses dynamic member {value}");
            }
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

    private string? ParentOfCall(MetadataReader md, Instruction instruction)
    {
        var handle = MetadataTokens.EntityHandle(instruction.Operand);
        return handle.Kind == HandleKind.MemberReference
            ? ParentTypeName(md, md.GetMemberReference((MemberReferenceHandle)handle).Parent)
            : null;
    }

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

    private readonly record struct Instruction(ILOpCode OpCode, int Operand);

    private static readonly ImmutableDictionary<ushort, OperandType> OperandTypes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToImmutableDictionary(o => unchecked((ushort)o.Value), o => o.OperandType);

    private static List<Instruction> Decode(byte[] il)
    {
        var instructions = new List<Instruction>();
        var offset = 0;
        while (offset < il.Length)
        {
            ushort value = il[offset++];
            if (value == 0xFE)
                value = (ushort)(0xFE00 | il[offset++]);
            var operandType = OperandTypes[value];
            var operand = 0;
            switch (operandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    operand = il[offset];
                    offset += 1;
                    break;
                case OperandType.InlineVar:
                    offset += 2;
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    offset += 8;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, offset);
                    offset += 4 + count * 4;
                    break;
                default:
                    operand = BitConverter.ToInt32(il, offset);
                    offset += 4;
                    break;
            }
            instructions.Add(new Instruction((ILOpCode)value, operand));
        }
        return instructions;
    }
}
