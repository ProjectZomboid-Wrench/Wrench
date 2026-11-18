using System.Dynamic;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CSharp.RuntimeBinder;
using System.Linq.Expressions;
using Wrench.Host.Mods;
using CSharpBinder = Microsoft.CSharp.RuntimeBinder.Binder;

namespace Wrench.Host.Security;

/// <summary>
/// Stand-in for <see cref="CSharpBinder"/> that restricted mods' binder calls are rewritten to at load time
/// (<see cref="BinderRewriter"/>). Same signatures; each call site's binder is wrapped in a <see cref="GatedBinder"/>.
/// </summary>
public static class DynamicBinder
{
    public static CallSiteBinder BinaryOperation(CSharpBinderFlags flags, ExpressionType operation, Type? context,
        IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.BinaryOperation(flags, operation, context, argumentInfo), context);

    public static CallSiteBinder Convert(CSharpBinderFlags flags, Type type, Type? context) =>
        Gate(CSharpBinder.Convert(flags, type, context), context);

    public static CallSiteBinder GetIndex(CSharpBinderFlags flags, Type? context, IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.GetIndex(flags, context, argumentInfo), context);

    public static CallSiteBinder GetMember(CSharpBinderFlags flags, string name, Type? context,
        IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.GetMember(flags, name, context, argumentInfo), context);

    public static CallSiteBinder Invoke(CSharpBinderFlags flags, Type? context, IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.Invoke(flags, context, argumentInfo), context);

    public static CallSiteBinder InvokeConstructor(CSharpBinderFlags flags, Type? context,
        IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.InvokeConstructor(flags, context, argumentInfo), context);

    public static CallSiteBinder InvokeMember(CSharpBinderFlags flags, string name, IEnumerable<Type>? typeArguments,
        Type? context, IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.InvokeMember(flags, name, typeArguments, context, argumentInfo), context);

    public static CallSiteBinder IsEvent(CSharpBinderFlags flags, string name, Type? context) =>
        Gate(CSharpBinder.IsEvent(flags, name, context), context);

    public static CallSiteBinder SetIndex(CSharpBinderFlags flags, Type? context, IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.SetIndex(flags, context, argumentInfo), context);

    public static CallSiteBinder SetMember(CSharpBinderFlags flags, string name, Type? context,
        IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.SetMember(flags, name, context, argumentInfo), context);

    public static CallSiteBinder UnaryOperation(CSharpBinderFlags flags, ExpressionType operation, Type? context,
        IEnumerable<CSharpArgumentInfo>? argumentInfo) =>
        Gate(CSharpBinder.UnaryOperation(flags, operation, context, argumentInfo), context);

    // The context decides accessibility for the C# binder, so it has to be one of the calling mod's own types:
    // typeof(SomeWrenchType) would open up that assembly's internals.
    private static CallSiteBinder Gate(CallSiteBinder inner, Type? context)
    {
        if (context is null || AssemblyLoadContext.GetLoadContext(context.Assembly) is not ModLoadContext owner)
            throw new RuntimeBinderException("Wrench: dynamic call sites must be created from mod code");
        return new GatedBinder((DynamicMetaObjectBinder)inner, owner);
    }
}

/// <summary>
/// Binds through the C# binder, then checks every member the resulting rule would call against
/// <see cref="DynamicPolicy"/> before the call site may run it. Nested dynamic operations are gated the same way.
/// </summary>
internal sealed class GatedBinder(DynamicMetaObjectBinder inner, ModLoadContext owner) : DynamicMetaObjectBinder
{
    public override Type ReturnType => inner.ReturnType;

    public override DynamicMetaObject Bind(DynamicMetaObject target, DynamicMetaObject[] args)
    {
        var bound = inner.Bind(target, args);
        var expression = DynamicPolicy.Check(bound.Expression, owner);
        DynamicPolicy.Check(bound.Restrictions.ToExpression(), owner, allowNested: false);
        return new DynamicMetaObject(expression, bound.Restrictions);
    }
}
