using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Wrench.Host.Tests;

internal static class ModCompiler
{
    private const string Usings = "using System; using System.Collections.Generic; using System.Linq; using Wrench;\n";

    public static string Compile(string source, string name, bool allowUnsafe = false, string? fileName = null)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(ModInstance).Assembly.Location)
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(Usings + source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe,
                nullableContextOptions: NullableContextOptions.Enable));

        var directory = Directory.CreateTempSubdirectory("wrench-vetting-").FullName;
        var path = Path.Combine(directory, (fileName ?? name) + ".dll");
        var result = compilation.Emit(path);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }
}
