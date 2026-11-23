using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Analyzers.Tests;

[TestClass]
public sealed class SerialTestResourceAnalyzerTests
{
    private const string SupportSource = """
        using System;
        using TestSupport;

        [assembly: SerialTestMember("Hooks", typeof(Channel<>), nameof(Channel<object>.Subscribe))]

        namespace TestSupport
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class SerialTestResourceAttribute(string name) : Attribute
            {
                public string Name { get; } = name;
            }

            [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
            public sealed class SerialTestMemberAttribute(string resourceName, Type type, string memberName) : Attribute
            {
                public string ResourceName { get; } = resourceName;
                public Type Type { get; } = type;
                public string MemberName { get; } = memberName;
            }

            [SerialTestResource("Fonts")]
            public static class Fonts
            {
                public static readonly object Shared = new();
            }

            public sealed class Channel<T>
            {
                public void Subscribe() { }
                public void Other() { }
            }

            public sealed class TestClassAttribute : Attribute;
            public sealed class TestMethodAttribute : Attribute;
            public sealed class TestInitializeAttribute : Attribute;
            public sealed class DoNotParallelizeAttribute : Attribute;
        }

        """;

    private const string Helpers = """
        internal static class Helper
        {
            public static readonly object HeldFonts = TestSupport.Fonts.Shared;
            public static object Direct() => TestSupport.Fonts.Shared;
            public static object Indirect() => Direct();
            public static object Through => Indirect();
            public static object Clean() => new object();
            public static System.Func<object> Later() => () => Direct();
        }

        internal sealed class HoldsFonts
        {
            private readonly object _fonts = TestSupport.Fonts.Shared;
        }

        """;

    private static string TestClass(string classAttributes, string members) => $$"""
        {{classAttributes}}
        [TestSupport.TestClass]
        public sealed class SampleTests
        {
            {{members}}
        }
        """;

    private static string TestWithBody(string body) => $"[TestSupport.TestMethod] public void Sample() {{ {body} }}";

    [TestMethod]
    [DataRow("_ = TestSupport.Fonts.Shared;", DisplayName = "Named directly")]
    [DataRow("Helper.Direct();", DisplayName = "Through a helper")]
    [DataRow("Helper.Indirect();", DisplayName = "Through two helpers")]
    [DataRow("_ = Helper.Through;", DisplayName = "Through a property")]
    [DataRow("_ = Helper.HeldFonts;", DisplayName = "Through a field's initializer")]
    [DataRow("_ = new HoldsFonts();", DisplayName = "Through an instance field's initializer")]
    [DataRow("System.Func<object> later = () => Helper.Direct();", DisplayName = "Inside a lambda")]
    [DataRow("Helper.Later();", DisplayName = "Through a helper's lambda")]
    [DataRow("new TestSupport.Channel<int>().Subscribe();", DisplayName = "A marked member of another type")]
    public void AParallelTestReachingAResource_ReportsCad0004(string body)
    {
        var diagnostics = Analyze(TestClass(string.Empty, TestWithBody(body)));

        Assert.HasCount(1, diagnostics, string.Join(Environment.NewLine, diagnostics));
        Assert.AreEqual(SerialTestResourceAnalyzer.DiagnosticId, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), "SampleTests.Sample");
    }

    [TestMethod]
    public void TheError_NamesTheResourceAndTheWayThere()
    {
        var message = Analyze(TestClass(string.Empty, TestWithBody("Helper.Indirect();"))).Single().GetMessage();

        StringAssert.Contains(message, "'Fonts'");
        StringAssert.Contains(message, "SampleTests.Sample -> Helper.Indirect -> Helper.Direct");
    }

    [TestMethod]
    [DataRow("[TestSupport.TestInitialize] public void SetUp() => Helper.Direct(); [TestSupport.TestMethod] public void Sample() { }", DisplayName = "A TestInitialize")]
    [DataRow("private readonly object _fonts = Helper.Direct(); [TestSupport.TestMethod] public void Sample() { }", DisplayName = "An instance field of the test class")]
    [DataRow("public SampleTests() => Helper.Direct(); [TestSupport.TestMethod] public void Sample() { }", DisplayName = "The test class's constructor")]
    public void WhatEveryTestRuns_CountsForEachTest(string members)
    {
        var diagnostics = Analyze(TestClass(string.Empty, members));

        Assert.HasCount(1, diagnostics, string.Join(Environment.NewLine, diagnostics));
    }

    [TestMethod]
    [DataRow("[TestSupport.DoNotParallelize]", "[TestSupport.TestMethod] public void Sample() => Helper.Direct();", DisplayName = "A serial class")]
    [DataRow("", "[TestSupport.TestMethod, TestSupport.DoNotParallelize] public void Sample() => Helper.Direct();", DisplayName = "A serial method")]
    [DataRow("", "[TestSupport.TestMethod] public void Sample() => Helper.Clean();", DisplayName = "A helper reaching nothing")]
    [DataRow("", "[TestSupport.TestMethod] public void Sample() => new TestSupport.Channel<int>().Other();", DisplayName = "An unmarked member of the same type")]
    [DataRow("", "public void NotATest() => Helper.Direct();", DisplayName = "A method that isn't a test")]
    public void WhatIsAllowed_ReportsNothing(string classAttributes, string members)
    {
        var diagnostics = Analyze(TestClass(classAttributes, members));

        Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));
    }

    [TestMethod]
    public void OnlyTheTestThatReachesIt_IsReported()
    {
        var diagnostics = Analyze(TestClass(string.Empty, """
            [TestSupport.TestMethod] public void Reaches() => Helper.Direct();
            [TestSupport.TestMethod] public void DoesNot() => Helper.Clean();
            """));

        StringAssert.Contains(diagnostics.Single().GetMessage(), "SampleTests.Reaches");
    }

    [TestMethod]
    public void ACompilationWithoutTheAttribute_IsNotAnalyzed()
    {
        const string Source = """
            public sealed class TestClassAttribute : System.Attribute;
            public sealed class TestMethodAttribute : System.Attribute;

            [TestClass]
            public sealed class SampleTests
            {
                [TestMethod] public void Sample() { }
            }
            """;

        Assert.IsEmpty(AnalyzeSource(Source));
    }

    private static ImmutableArray<Diagnostic> Analyze(string testSource) => AnalyzeSource(SupportSource + Helpers + testSource);

    private static ImmutableArray<Diagnostic> AnalyzeSource(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Sample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var compilerErrors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.IsEmpty(compilerErrors, "The sample must compile: " + string.Join(Environment.NewLine, compilerErrors));

        return compilation
            .WithAnalyzers([new SerialTestResourceAnalyzer()])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }

    private static IEnumerable<MetadataReference> PlatformReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path));
}
