using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CSharp.RuntimeBinder;
using Wrench.Host.Logging;
using Wrench.Host.Mods;

namespace Wrench.Host.Security;

/// <summary>
/// The run-time half of <see cref="ModPolicy"/>, for <c>dynamic</c>: what a bound call site may reach is only known
/// once the receiver's type is, so every member in a bound rule is checked the way <see cref="AssemblyInspector"/>
/// checks a member reference: the mod's own code, public Wrench API, or allowed framework members whose signatures
/// only mention allowed types.
/// </summary>
internal static class DynamicPolicy
{
    private static readonly Logger Log = Logging.Log.For("Wrench.Host");

    private static readonly string FrameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

    private static readonly HashSet<Assembly> WrenchAssemblies =
        [typeof(ModInstance).Assembly, typeof(Zomboid.Java.JavaObject).Assembly, typeof(Zomboid.Lua.LuaObject).Assembly];

    // DynamicObject's rules call its virtual Try* methods, which GameObject overrides. Mods can't derive from it.
    private static readonly HashSet<string> DynamicObjectDispatch =
    [
        nameof(DynamicObject.TryGetMember), nameof(DynamicObject.TrySetMember), nameof(DynamicObject.TryDeleteMember),
        nameof(DynamicObject.TryInvokeMember), nameof(DynamicObject.TryConvert), nameof(DynamicObject.TryCreateInstance),
        nameof(DynamicObject.TryInvoke), nameof(DynamicObject.TryBinaryOperation), nameof(DynamicObject.TryUnaryOperation),
        nameof(DynamicObject.TryGetIndex), nameof(DynamicObject.TrySetIndex), nameof(DynamicObject.TryDeleteIndex),
    ];

    /// <summary>Throws <see cref="RuntimeBinderException"/> if the rule reaches anything denied; returns it with nested dynamic operations gated.</summary>
    public static Expression Check(Expression rule, ModLoadContext owner, bool allowNested = true) =>
        new Visitor(owner, allowNested).Visit(rule);

    /// <summary>Why <paramref name="member"/> is denied to <paramref name="owner"/>'s code, or null if it's allowed.</summary>
    public static string? Deny(MemberInfo member, ModLoadContext owner)
    {
        if (member is MethodInfo { IsGenericMethod: true } generic && generic.GetGenericArguments().FirstOrDefault(t => !IsTypeAllowed(t, owner)) is { } argument)
            return $"type argument {argument}";
        var declaring = member.DeclaringType;
        if (declaring is null)
            return "a global member";
        if (declaring.GetGenericArguments().FirstOrDefault(t => !IsTypeAllowed(t, owner)) is { } typeArgument)
            return $"type argument {typeArgument}";

        var assembly = declaring.Assembly;
        if (AssemblyLoadContext.GetLoadContext(assembly) == owner)
            return null;
        if (WrenchAssemblies.Contains(assembly))
            return IsPublic(member) ? null : "a non-public Wrench member";
        if (!IsFramework(assembly))
            return $"{assembly.GetName().Name}, which isn't framework, Wrench API or the mod's own";
        if (!IsPublic(member))
            return "a non-public framework member";

        var typeName = MetadataName(declaring);
        if (declaring == typeof(DynamicObject) && DynamicObjectDispatch.Contains(member.Name))
            return null;
        if (!IsFrameworkTypeAllowed(declaring) && !ModPolicy.RestrictedTypes.ContainsKey(typeName))
            return "a type mods may not use";

        foreach (var name in MetadataNames(member))
        {
            if (ModPolicy.DeniedMembers.Contains(name))
                return "a member that leads to reflection";
            if (ModPolicy.RestrictedTypes.TryGetValue(typeName, out var allowed) && !allowed.Contains(name))
                return "a member of a restricted type";
        }
        return SignatureTypes(member).FirstOrDefault(t => !IsSignatureTypeAllowed(t, owner)) is { } denied
            ? $"a member whose signature uses {denied}"
            : null;
    }

    private static bool IsPublic(MemberInfo member) =>
        member.DeclaringType!.IsVisible && member switch
        {
            MethodBase m => m.IsPublic,
            FieldInfo f => f.IsPublic,
            PropertyInfo p => p.GetAccessors(nonPublic: false).Length > 0,
            EventInfo e => e.AddMethod?.IsPublic == true,
            _ => false,
        };

    private static IEnumerable<string> MetadataNames(MemberInfo member) => member switch
    {
        PropertyInfo p => p.GetAccessors(nonPublic: true).Select(a => a.Name),
        _ => [member.Name],
    };

    private static IEnumerable<Type> SignatureTypes(MemberInfo member) => member switch
    {
        MethodInfo m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType),
        ConstructorInfo c => c.GetParameters().Select(p => p.ParameterType),
        PropertyInfo p => p.GetIndexParameters().Select(i => i.ParameterType).Append(p.PropertyType),
        FieldInfo f => [f.FieldType],
        EventInfo e => e.EventHandlerType is { } handler ? [handler] : [],
        _ => [],
    };

    // As in AssemblyInspector, Type may appear in signatures; only its restricted members are usable.
    private static bool IsSignatureTypeAllowed(Type type, ModLoadContext owner) =>
        IsTypeAllowed(type, owner, element => element == typeof(Type) || element == typeof(RuntimeTypeHandle));

    private static bool IsTypeAllowed(Type type, ModLoadContext owner, Func<Type, bool>? alsoAllowed = null)
    {
        while (type.HasElementType)
        {
            if (type.IsPointer || type.IsFunctionPointer)
                return false;
            type = type.GetElementType()!;
        }
        if (type.IsGenericParameter)
            return true;
        if (type.IsGenericType && !type.GetGenericArguments().All(t => IsTypeAllowed(t, owner, alsoAllowed)))
            return false;

        var assembly = type.Assembly;
        if (AssemblyLoadContext.GetLoadContext(assembly) == owner)
            return true;
        if (WrenchAssemblies.Contains(assembly))
            return type.IsVisible;
        return IsFramework(assembly) && (IsFrameworkTypeAllowed(type) || alsoAllowed?.Invoke(type) == true);
    }

    private static bool IsFrameworkTypeAllowed(Type type)
    {
        var outermost = type;
        while (outermost.DeclaringType is not null)
            outermost = outermost.DeclaringType;
        return ModPolicy.IsTypeAllowed(outermost.Namespace ?? "", MetadataName(type));
    }

    private static bool IsFramework(Assembly assembly) =>
        AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default && !assembly.IsDynamic
        && string.Equals(Path.GetDirectoryName(assembly.Location), FrameworkDirectory, StringComparison.Ordinal);

    /// <summary>The name <see cref="ModPolicy"/> lists types by: <c>Namespace.Name`Arity</c>, nested with <c>/</c>.</summary>
    private static string MetadataName(Type type)
    {
        if (type.IsGenericType)
            type = type.GetGenericTypeDefinition();
        if (type.DeclaringType is { } outer)
            return MetadataName(outer) + "/" + type.Name;
        return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
    }

    private sealed class Visitor(ModLoadContext owner, bool allowNested) : ExpressionVisitor
    {
        private void Require(MemberInfo? member)
        {
            if (member is null || Deny(member, owner) is not { } reason)
                return;
            var name = $"{member.DeclaringType}::{member.Name}";
            Log.Warn($"{owner.Name}: denied dynamic use of {name} ({reason})");
            throw new RuntimeBinderException($"Wrench: mods may not use {name} ({reason})");
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            Require(node.Method);
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            Require(node.Member);
            return base.VisitMember(node);
        }

        protected override Expression VisitNew(NewExpression node)
        {
            Require(node.Constructor);
            return base.VisitNew(node);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            Require(node.Method);
            return base.VisitUnary(node);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            Require(node.Method);
            return base.VisitBinary(node);
        }

        protected override Expression VisitIndex(IndexExpression node)
        {
            Require(node.Indexer);
            return base.VisitIndex(node);
        }

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            Require(node.Expression.Type.GetMethod("Invoke"));
            return base.VisitInvocation(node);
        }

        protected override MemberAssignment VisitMemberAssignment(MemberAssignment node)
        {
            Require(node.Member);
            return base.VisitMemberAssignment(node);
        }

        protected override ElementInit VisitElementInit(ElementInit node)
        {
            Require(node.AddMethod);
            return base.VisitElementInit(node);
        }

        protected override Expression VisitSwitch(SwitchExpression node)
        {
            Require(node.Comparison);
            return base.VisitSwitch(node);
        }

        // Binders defer what they can't bind yet (DynamicObject falling back to invoking a member's value) to a
        // nested call site with an ungated binder of their own.
        protected override Expression VisitDynamic(DynamicExpression node)
        {
            if (!allowNested || node.Binder is not DynamicMetaObjectBinder binder)
                throw new RuntimeBinderException("Wrench: unexpected nested dynamic operation");
            var gated = binder is GatedBinder ? binder : new GatedBinder(binder, owner);
            return Expression.MakeDynamic(node.DelegateType, gated, Visit(node.Arguments));
        }

        // Some framework nodes dispatch here without being NodeType.Extension, so ReduceExtensions returns them as is.
        protected override Expression VisitExtension(Expression node)
        {
            var reduced = node.CanReduce ? node.Reduce() : node;
            if (ReferenceEquals(reduced, node))
                throw new RuntimeBinderException($"Wrench: unexpected {node.GetType().FullName} ({node.NodeType}) in a dynamic rule");
            return Visit(reduced);
        }
    }
}
