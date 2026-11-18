using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Wrench.Host.Security;

/// <summary>
/// Points a restricted mod's calls to the C# runtime binder at <see cref="DynamicBinder"/>, so every <c>dynamic</c>
/// call site it creates is gated by <see cref="DynamicPolicy"/>. Vetting has already ensured that the binder is only
/// ever reached through direct calls to its known factory methods.
/// </summary>
internal static class BinderRewriter
{
    private const string BinderType = "Microsoft.CSharp.RuntimeBinder.Binder";

    private static readonly Dictionary<string, MethodInfo> Shims = typeof(DynamicBinder)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(m => m.Name);

    /// <summary>The rewritten assembly and its symbols, if it had any; null when there was nothing to rewrite.</summary>
    public static (byte[] Assembly, byte[]? Symbols)? Rewrite(string path)
    {
        var symbolsPath = Path.ChangeExtension(path, ".pdb");
        var readSymbols = File.Exists(symbolsPath);
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true, ReadSymbols = readSymbols });

        var rewritten = false;
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is not MethodReference target || target.DeclaringType.FullName != BinderType)
                        continue;
                    if (instruction.OpCode != OpCodes.Call || !Shims.TryGetValue(target.Name, out var shim)
                        || shim.GetParameters().Length != target.Parameters.Count)
                        throw new InvalidOperationException($"{method.FullName} uses {BinderType}::{target.Name} in a way vetting should have rejected");
                    instruction.Operand = module.ImportReference(shim);
                    rewritten = true;
                }
            }
        }
        if (!rewritten)
            return null;

        using var assembly = new MemoryStream();
        using var symbols = readSymbols ? new MemoryStream() : null;
        module.Write(assembly, new WriterParameters
        {
            WriteSymbols = readSymbols, SymbolStream = symbols,
            SymbolWriterProvider = readSymbols ? new PortablePdbWriterProvider() : null,
        });
        return (assembly.ToArray(), symbols?.ToArray());
    }
}
