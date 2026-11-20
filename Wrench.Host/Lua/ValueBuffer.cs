using JNIBinding;
using JNIBinding.Bindings.Native.Types;

namespace Wrench.Host.Lua;

/// <summary>
/// Lua values passed between the host and the agent through WrenchLua's per-thread direct buffer, so scalars and
/// kinds need no JNI calls: an int count, then an int kind and 8 bytes of raw bits per value at <c>8 + 16 * i</c>.
/// Strings and objects travel as the JNI value beside it: null, the only one, or an <c>Object[]</c> of them in order;
/// a count of -1 means that <c>Object[]</c> holds every value instead.
/// </summary>
internal static unsafe class ValueBuffer
{
    private static _JClass _wrenchLua;
    private static _JMethodID _resultBuffer;
    [ThreadStatic] private static byte* _buffer;

    // Keep in sync with WrenchLua's kinds.
    private enum ValueKind
    {
        Nil, Double, Boolean, Int, Long, Float, Short, Byte, Char, String, Object,
    }

    public static void Initialize(JNIEnv env, _JClass wrenchLua)
    {
        _wrenchLua = wrenchLua;
        _resultBuffer = env.GetStaticMethodId(wrenchLua, "resultBuffer", "()Ljava/nio/ByteBuffer;");
    }

    /// <summary>Reads every value the agent wrote; takes ownership of <paramref name="references"/>.</summary>
    public static object?[] ReadAll(LuaScope scope, _JObject references)
    {
        var env = scope.Env;
        var buffer = Buffer(env);
        var count = *(int*)buffer;
        if (count < 0)
        {
            var all = JavaValues.ToDotnet(env, new _JObjectArray(references.Value), scope);
            env.DeleteLocalRef(references);
            return all;
        }
        if (count == 0)
            return [];

        var objects = 0;
        for (var i = 0; i < count; i++)
        {
            if (Kind(buffer, i) >= ValueKind.String)
                objects++;
        }
        var values = new object?[count];
        var next = 0;
        for (var i = 0; i < count; i++)
        {
            var kind = Kind(buffer, i);
            if (kind < ValueKind.String)
                values[i] = Scalar(kind, Bits(buffer, i));
            else
                values[i] = Reference(scope, kind,
                    objects == 1 ? references : env.GetObjectArrayElement(new _JObjectArray(references.Value), next++));
        }
        if (objects > 1)
            env.DeleteLocalRef(references);
        return values;
    }

    /// <summary>Reads the single value the agent wrote; takes ownership of <paramref name="reference"/>.</summary>
    public static object? ReadOne(LuaScope scope, _JObject reference)
    {
        var buffer = Buffer(scope.Env);
        var kind = Kind(buffer, 0);
        return kind < ValueKind.String ? Scalar(kind, Bits(buffer, 0)) : Reference(scope, kind, reference);
    }

    /// <summary>Writes no values, for a .NET function that returns nothing.</summary>
    public static void WriteNone(JNIEnv env) => *(int*)Buffer(env) = 0;

    /// <summary>
    /// Writes <paramref name="value"/> for the agent to read, returning the reference to hand back beside it (null for
    /// scalars). New refs made converting it are recorded in <paramref name="temps"/>, except the returned one.
    /// </summary>
    public static _JObject WriteOne(LuaScope scope, object? value, ModInstance? mod, LuaBridge.Temps temps)
    {
        if (value is System.Runtime.CompilerServices.IUnion union)
            value = union.Value;
        var (kind, bits) = value switch
        {
            null => (ValueKind.Nil, 0L),
            bool flag => (ValueKind.Boolean, flag ? 1L : 0L),
            sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal =>
                (ValueKind.Double, BitConverter.DoubleToInt64Bits(Convert.ToDouble(value))),
            _ => (ValueKind.Object, 0L),
        };
        var reference = kind == ValueKind.Object ? temps.Detach(LuaBridge.ToJava(scope, value, mod, temps)) : _JObject.Null;
        var buffer = Buffer(scope.Env);
        *(int*)buffer = 1;
        *(int*)(buffer + 8) = (int)kind;
        *(long*)(buffer + 16) = bits;
        return reference;
    }

    private static byte* Buffer(JNIEnv env)
    {
        if (_buffer is null)
        {
            // WrenchLua's ThreadLocal keeps the buffer alive, so its address stays valid for the thread.
            var buffer = env.CallStaticObjectMethod(_wrenchLua, _resultBuffer, null);
            _buffer = (byte*)env.GetDirectBufferAddress(buffer);
            env.DeleteLocalRef(buffer);
        }
        return _buffer;
    }

    private static ValueKind Kind(byte* buffer, int index) => (ValueKind)(*(int*)(buffer + 8 + 16 * index));

    private static long Bits(byte* buffer, int index) => *(long*)(buffer + 16 + 16 * index);

    private static object? Scalar(ValueKind kind, long bits) => kind switch
    {
        ValueKind.Nil => null,
        ValueKind.Double => BitConverter.Int64BitsToDouble(bits),
        ValueKind.Boolean => bits != 0,
        ValueKind.Int => (int)bits,
        ValueKind.Long => bits,
        ValueKind.Float => BitConverter.Int32BitsToSingle((int)bits),
        ValueKind.Short => (short)bits,
        ValueKind.Byte => (sbyte)bits,
        ValueKind.Char => (char)bits,
        _ => throw new InvalidOperationException($"Unknown value kind {kind}"),
    };

    private static object Reference(LuaScope scope, ValueKind kind, _JObject obj)
    {
        if (kind == ValueKind.Object)
            return new GameObject(scope, obj.Value);
        var text = scope.Env.AsString(obj.AsString());
        scope.Env.DeleteLocalRef(obj);
        return text;
    }
}
