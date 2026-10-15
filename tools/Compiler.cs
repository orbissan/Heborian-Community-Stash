using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HeborianBuild;

public static class Compiler
{
    public static void Compile(string[] sources, string[] references, string output, string framework, string version)
    {
        var trees = sources.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path, encoding: Encoding.UTF8)).ToList();
        trees.Add(CSharpSyntaxTree.ParseText(
            "[assembly: System.Reflection.AssemblyVersion(\"" + version + ".0\")]\n" +
            "[assembly: System.Reflection.AssemblyFileVersion(\"" + version + ".0\")]\n" +
            "[assembly: System.Runtime.Versioning.TargetFramework(\"" + framework + "\")]"));
        var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(output), trees,
            references.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, deterministic: true));
        using var stream = File.Create(output);
        var result = compilation.Emit(stream);
        // BepInEx 5 references CLR 2.0; Unity Mono supplies CLR 4.0. Report each distinct warning once.
        foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString()).Distinct()) Console.WriteLine(diagnostic);
        if (!result.Success) throw new InvalidOperationException("Compilation failed: " + output);
    }
}
