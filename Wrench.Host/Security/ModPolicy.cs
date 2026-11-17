namespace Wrench.Host.Security;

/// <summary>
/// What a restricted mod may reference. The aim is parity with a Lua mod: game access goes through Wrench.Api (and
/// so through Kahlua's exposed classes), plus the parts of the BCL needed to write ordinary C#. Anything not listed,
/// such as I/O, networking, processes, threads, reflection and interop, is rejected.
/// </summary>
internal static class ModPolicy
{
    /// <summary>Namespaces whose types are all allowed. Matched exactly: <c>System.Text</c> doesn't admit <c>System.Text.RegularExpressions</c>.</summary>
    public static readonly HashSet<string> AllowedNamespaces =
    [
        "Wrench",
        "System.Collections",
        "System.Collections.Generic",
        "System.Collections.ObjectModel",
        "System.Collections.Concurrent",
        "System.Linq",
        "System.Text",
        "System.Globalization",
        "System.Diagnostics.CodeAnalysis",
    ];

    /// <summary>Individually allowed types, as metadata names (<c>Namespace.Name`Arity</c>, nested with <c>/</c>).</summary>
    public static readonly HashSet<string> AllowedTypes =
    [
        // Core
        "System.Object", "System.ValueType", "System.Enum", "System.Array", "System.String", "System.Boolean",
        "System.Char", "System.SByte", "System.Byte", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
        "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal", "System.IntPtr",
        "System.UIntPtr", "System.Void", "System.Nullable`1", "System.Math", "System.MathF", "System.Convert",
        "System.Random", "System.DateTime", "System.DateTimeOffset", "System.DayOfWeek", "System.TimeSpan",
        "System.Guid", "System.Index", "System.Range", "System.Lazy`1", "System.Span`1", "System.ReadOnlySpan`1",
        "System.Memory`1", "System.ReadOnlyMemory`1", "System.StringComparison", "System.StringComparer",
        "System.StringSplitOptions", "System.MidpointRounding", "System.IDisposable", "System.IEquatable`1",
        "System.IComparable", "System.IComparable`1", "System.IFormattable", "System.ISpanFormattable",
        "System.IFormatProvider", "System.FormattableString", "System.Delegate", "System.MulticastDelegate",
        "System.Predicate`1", "System.Comparison`1", "System.Converter`2", "System.EventHandler", "System.EventHandler`1",
        "System.EventArgs", "System.Attribute", "System.AttributeUsageAttribute", "System.AttributeTargets",
        "System.FlagsAttribute", "System.ObsoleteAttribute", "System.ParamArrayAttribute", "System.Exception",
        "System.Diagnostics.UnreachableException",

        // Compiler-emitted
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute",
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.TupleElementNamesAttribute",
        "System.Runtime.CompilerServices.DynamicAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.IsExternalInit",
        "System.Runtime.CompilerServices.RequiredMemberAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
        "System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute",
        // Thrown by exhaustive switch expressions (e.g. over a union) when no arm matches.
        "System.Runtime.CompilerServices.SwitchExpressionException",
        "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler",
        "System.Runtime.CompilerServices.FormattableStringFactory",
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
        "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
        "System.Runtime.CompilerServices.RuntimeHelpers",
        "System.Runtime.InteropServices.InAttribute",
        "System.Runtime.InteropServices.OutAttribute",
        "System.Runtime.InteropServices.OptionalAttribute",
        "System.Runtime.Versioning.TargetFrameworkAttribute",
        "System.Diagnostics.DebuggableAttribute",
        "System.Diagnostics.DebuggableAttribute/DebuggingModes",
        "System.Diagnostics.DebuggerBrowsableAttribute",
        "System.Diagnostics.DebuggerBrowsableState",
        "System.Diagnostics.DebuggerHiddenAttribute",
        "System.Diagnostics.DebuggerStepThroughAttribute",
        "System.Diagnostics.DebuggerDisplayAttribute",
        "System.Diagnostics.DebuggerNonUserCodeAttribute",
        "System.Reflection.AssemblyCompanyAttribute",
        "System.Reflection.AssemblyConfigurationAttribute",
        "System.Reflection.AssemblyCopyrightAttribute",
        "System.Reflection.AssemblyDescriptionAttribute",
        "System.Reflection.AssemblyFileVersionAttribute",
        "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Reflection.AssemblyMetadataAttribute",
        "System.Reflection.AssemblyProductAttribute",
        "System.Reflection.AssemblyTitleAttribute",
        "System.Reflection.AssemblyTrademarkAttribute",
        "System.Reflection.AssemblyVersionAttribute",
        "System.Reflection.DefaultMemberAttribute",

        // `dynamic`: call sites and the C# runtime binder. Member names used through it are checked separately.
        "System.Runtime.CompilerServices.CallSite",
        "System.Runtime.CompilerServices.CallSite`1",
        "System.Runtime.CompilerServices.CallSiteBinder",
        "Microsoft.CSharp.RuntimeBinder.Binder",
        "Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo",
        "Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfoFlags",
        "Microsoft.CSharp.RuntimeBinder.CSharpBinderFlags",
        "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException",
        "System.Linq.Expressions.ExpressionType",
    ];

    /// <summary>Allowed types whose members are otherwise denied: only these members may be used.</summary>
    public static readonly Dictionary<string, HashSet<string>> RestrictedTypes = new()
    {
        ["System.Runtime.CompilerServices.RuntimeHelpers"] = ["InitializeArray", "EnsureSufficientExecutionStack"],
        ["System.RuntimeTypeHandle"] = [],
    };

    /// <summary>
    /// Member names that lead to reflection, denied on every framework type. Matched by name rather than declaring
    /// type because IL can reference an inherited member through a derived type (ArgumentException::get_TargetSite).
    /// </summary>
    public static readonly HashSet<string> DeniedMembers =
    [
        "GetType", "get_TargetSite", "get_Method", "get_Target", "GetMethodImpl", "DynamicInvoke", "CreateDelegate",
        "GetInvocationList",
    ];

    /// <summary>
    /// Member names a <c>dynamic</c> call site may not bind to. The only reflective objects a restricted mod can get
    /// hold of are Types (typeof), exceptions it catches and delegates it makes, so this is every member of those
    /// types that hands out something reflective: reflection objects, runtime handles, pointers or plain object.
    /// Members returning strings, numbers, booleans or enums (Type.Name) stay usable.
    /// </summary>
    public static readonly HashSet<string> DeniedDynamicMembers = BuildDeniedDynamicMembers();

    private static HashSet<string> BuildDeniedDynamicMembers()
    {
        Type[] reachable = [typeof(Type), typeof(Exception), typeof(Delegate), typeof(MulticastDelegate), typeof(object), typeof(Array)];
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "InvokeMember", "DynamicInvoke", "CreateDelegate", "GetObjectData", "MemberwiseClone",
        };
        foreach (var type in reachable)
        {
            foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                                                   | System.Reflection.BindingFlags.Static))
            {
                var returned = member switch
                {
                    System.Reflection.MethodInfo m => m.ReturnType,
                    System.Reflection.PropertyInfo p => p.PropertyType,
                    System.Reflection.FieldInfo f => f.FieldType,
                    _ => null,
                };
                if (returned is not null && IsReflective(returned))
                    names.Add(member.Name);
            }
        }
        return names;
    }

    private static bool IsReflective(Type type)
    {
        while (type.HasElementType)
            type = type.GetElementType()!;
        if (type.IsGenericType)
            return type.GetGenericArguments().Any(IsReflective);
        return type == typeof(object) || type == typeof(Type) || typeof(Delegate).IsAssignableFrom(type)
               || type.Namespace == "System.Reflection" || type.IsPointer || type == typeof(IntPtr)
               || type == typeof(RuntimeTypeHandle) || type == typeof(RuntimeMethodHandle) || type == typeof(RuntimeFieldHandle);
    }

    public static bool IsTypeAllowed(string ns, string fullName) =>
        AllowedNamespaces.Contains(ns) || AllowedTypes.Contains(fullName) || IsSystemException(ns, fullName)
        || IsActionOrFunc(fullName);

    private static bool IsActionOrFunc(string fullName) =>
        fullName == "System.Action" || fullName.StartsWith("System.Action`", StringComparison.Ordinal)
        || fullName.StartsWith("System.Func`", StringComparison.Ordinal)
        || fullName.StartsWith("System.ValueTuple", StringComparison.Ordinal)
        || fullName.StartsWith("System.Tuple", StringComparison.Ordinal);

    // Exception types in the System namespace (ArgumentException etc.) are data carriers; members like TargetSite
    // are denied on the base class.
    private static bool IsSystemException(string ns, string fullName) =>
        ns == "System" && fullName.EndsWith("Exception", StringComparison.Ordinal) && !fullName.Contains('/');
}
