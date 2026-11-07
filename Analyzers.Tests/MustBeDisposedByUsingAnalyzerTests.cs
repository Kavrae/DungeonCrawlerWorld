using System.Collections.Immutable;
using Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Analyzers.Tests;

[TestClass]
public sealed class MustBeDisposedByUsingAnalyzerTests
{
    private const string Declarations = """
        using System;

        namespace Sample;

        [AttributeUsage(AttributeTargets.Struct)]
        public sealed class MustBeDisposedByUsingAttribute : Attribute;

        [MustBeDisposedByUsing]
        public ref struct Scope
        {
            private ref int _value;

            public Scope(ref int value) => _value = ref value;

            public bool Found => true;

            public ref int Value => ref _value;

            public void Dispose()
            {
            }
        }

        public ref struct Unmarked
        {
            public void Dispose()
            {
            }
        }

        public sealed class Pool
        {
            private int _value;

            public Scope Write() => new Scope(ref _value);

            public Scope TryWrite(bool found) => found ? Write() : default;

            public Unmarked Other() => default;
        }

        public static class Uses
        {
            private static void Take(Scope scope)
            {
            }

            public static void Run(Pool pool)
            {
                BODY
            }
        }
        """;

    [TestMethod]
    [DataRow("var write = pool.Write();", DisplayName = "A plain local")]
    [DataRow("pool.Write();", DisplayName = "A discarded call")]
    [DataRow("_ = pool.Write();", DisplayName = "A discard")]
    [DataRow("pool.Write().Value = 1;", DisplayName = "A chained member access")]
    [DataRow("Take(pool.Write());", DisplayName = "An argument")]
    [DataRow("var write = pool.TryWrite(true); using (write) { }", DisplayName = "A local disposed by a later using")]
    public void FlaggedShapes_ReportCad0001(string body)
    {
        var diagnostics = Analyze(body);

        Assert.HasCount(1, diagnostics, string.Join(Environment.NewLine, diagnostics));
        Assert.AreEqual(MustBeDisposedByUsingAnalyzer.DiagnosticId, diagnostics[0].Id);
    }

    [TestMethod]
    [DataRow("using var write = pool.Write(); write.Value = 1;", DisplayName = "A using declaration")]
    [DataRow("using (var write = pool.Write()) { write.Value = 1; }", DisplayName = "A using statement's declaration")]
    [DataRow("using (pool.Write()) { }", DisplayName = "A using statement's resource")]
    [DataRow("using var write = pool.TryWrite(false); if (!write.Found) { return; } write.Value = 1;", DisplayName = "TryWrite then an early return")]
    [DataRow("using var first = pool.Write(); using var second = pool.Write();", DisplayName = "Two using declarations")]
    [DataRow("Scope Make() => pool.Write(); using var write = Make();", DisplayName = "Returned from a local function")]
    [DataRow("var other = pool.Other();", DisplayName = "An unmarked type")]
    public void AllowedShapes_ReportNothing(string body)
    {
        var diagnostics = Analyze(body);

        Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));
    }

    private static ImmutableArray<Diagnostic> Analyze(string body)
    {
        var source = Declarations.Replace("BODY", body);
        var compilation = CSharpCompilation.Create(
            "Sample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var compilerErrors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.IsEmpty(compilerErrors, "The sample must compile: " + string.Join(Environment.NewLine, compilerErrors));

        return compilation
            .WithAnalyzers([new MustBeDisposedByUsingAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
}
