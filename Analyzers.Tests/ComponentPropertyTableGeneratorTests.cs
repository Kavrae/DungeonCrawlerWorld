using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Analyzers.ComponentProperties;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Analyzers.Tests;

/// <summary>The property-table generator: a sample holding every kind compiles, runs, and matches its snapshot; and each diagnostic.</summary>
[TestClass]
public sealed class ComponentPropertyTableGeneratorTests
{
    private const string EveryShape = """
        using System;
        using System.Collections.Generic;
        using Engine.ECS.Components;
        using Engine.ECS.Entities;
        using Engine.ECS.Properties;
        using Engine.Tags;

        namespace Microsoft.Xna.Framework
        {
            public struct Color
            {
                public uint PackedValue { get; set; }
            }
        }

        namespace Sample
        {
            public enum Mode : byte { First, Second }

            [Flags]
            public enum Marks : byte { None = 0, Left = 1, Right = 2 }

            [DescribedDefinition]
            public class Definition;

            public sealed class DerivedDefinition : Definition;

            [DescribedValue]
            public readonly partial record struct Point(int X, int Y);

            [DescribedByHand(typeof(HandAccess))]
            public readonly struct Hand(int value)
            {
                public int Value { get; } = value;
            }

            public static class HandAccess
            {
                private static readonly ComponentPropertyDescriptor ValueProperty = new(0, "Value", ComponentPropertyKind.Integer, typeof(int), default, ComponentPropertyFlags.ReadOnly);

                public static ComponentPropertyTable Description { get; } = new(typeof(Hand), [ValueProperty]);

                public static void ReadProperties(in Hand hand, ComponentPropertyVisitor visitor) => visitor.Integer(ValueProperty, hand.Value);

                public static bool WriteProperty(ref Hand hand, ReadOnlySpan<int> path, in ComponentPropertyValue value) => false;

                public static bool WriteProperty(ref Hand hand, ReadOnlySpan<int> path, in ComponentPropertyValue value, bool restoring) => false;
            }

            [DescribedComponent]
            public partial struct EveryShape : IScheduledTimer
            {
                private uint _timerWheelMark;
                private int _backing;

                public sbyte Small { get; set; }
                public ulong Large { get; set; }
                [PropertyMeaning(ComponentPropertyMeaningNames.Frame)]
                public uint Deadline { get; set; }
                public float Amount { get; set; }
                public double Precise { get; set; }
                public bool On { get; set; }
                public string Name { get; set; }
                public string? Note { get; set; }
                public Mode Mode { get; set; }
                public Marks Marks { get; set; }
                public Guid Id { get; set; }
                public Microsoft.Xna.Framework.Color Tint { get; set; }
                public EntityKey Owner { get; set; }
                public GameplayTag Condition { get; set; }
                public GameplayTagQuery? Accepts { get; set; }
                public Point At { get; set; }
                public Point? Maybe { get; set; }
                public Mode? MaybeMode { get; set; }
                public Hand Held { get; set; }
                public DerivedDefinition? Shared { get; set; }
                [ReadOnlyProperty]
                public int Fixed { get; init; }
                public int Initialized { get; init; }
                [ReadOnlyProperty]
                public int Locked { get; set; }
                [NotSaved]
                public int Scratch { get; set; }
                public readonly int Twice => Small * 2;
                public int Sentinel { readonly get => _backing - 1; set => _backing = value + 1; }
                public int Field;
                [NotAProperty]
                public int Ignored { get; set; }

                uint IScheduledTimer.NextTickFrame { readonly get => Deadline; set => Deadline = value; }
                uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }
            }

            [DescribedComponent]
            public readonly partial record struct WrittenWhole(Point At, int Count, Marks Marks);

            public static class Probe
            {
                public static string[] Failures()
                {
                    var failures = new List<string>();
                    var access = ComponentPropertyAccessOf<EveryShape>.Access;
                    if (access is null || ComponentPropertyAccessOf<WrittenWhole>.Access is null)
                    {
                        return ["the module initializer didn't register both components"];
                    }

                    var shape = new EveryShape { Small = 3, Large = 9, Name = "n", Note = "note", Mode = Mode.Second, Maybe = new Point(1, 2), Sentinel = 4, Field = 6 };
                    var text = new TextVisitor();
                    access.Read(shape, text);
                    Expect(failures, text.Output, "Small=3;Large=9;Deadline=0;Amount=0;Precise=0;On=False;Name=n;Note=note;Mode=1;Marks=0;");
                    Expect(failures, text.Output, "At{X=0;Y=0;}Maybe{X=1;Y=2;}MaybeMode=absent;Held{Value=0;}Shared=definition null;Fixed=0;Initialized=0;Locked=0;Scratch=0;Twice=6;Sentinel=4;Field=6;");

                    Write(failures, access, ref shape, [0], ComponentPropertyValue.FromInteger(5), true);
                    Write(failures, access, ref shape, [0], ComponentPropertyValue.FromInteger(200), false);
                    Write(failures, access, ref shape, [1], ComponentPropertyValue.FromInteger(-1), true);
                    Check(failures, shape.Large == ulong.MaxValue, "a ulong carried as its bits round-trips past long.MaxValue");
                    Write(failures, access, ref shape, [8], ComponentPropertyValue.FromEnum(7), false);
                    Write(failures, access, ref shape, [9], ComponentPropertyValue.FromEnum(4), false);
                    Write(failures, access, ref shape, [9], ComponentPropertyValue.FromEnum(3), true);
                    Write(failures, access, ref shape, [7], ComponentPropertyValue.Absent, true);
                    Write(failures, access, ref shape, [11], ComponentPropertyValue.FromColor(0xFF00FF00), true);
                    Write(failures, access, ref shape, [12], ComponentPropertyValue.FromInteger(42), true);
                    Write(failures, access, ref shape, [13], ComponentPropertyValue.FromTag(""), true);
                    Write(failures, access, ref shape, [14], ComponentPropertyValue.Absent, false);
                    Write(failures, access, ref shape, [15, 0], ComponentPropertyValue.FromInteger(8), true);
                    Write(failures, access, ref shape, [15], ComponentPropertyValue.FromInteger(8), false);
                    Write(failures, access, ref shape, [16], ComponentPropertyValue.Absent, true);
                    Write(failures, access, ref shape, [16, 0], ComponentPropertyValue.FromInteger(1), false);
                    Write(failures, access, ref shape, [17], ComponentPropertyValue.FromEnum(0), true);
                    Write(failures, access, ref shape, [18, 0], ComponentPropertyValue.FromInteger(1), false);
                    Write(failures, access, ref shape, [19], ComponentPropertyValue.Absent, false);
                    Write(failures, access, ref shape, [20], ComponentPropertyValue.FromInteger(1), false);
                    Write(failures, access, ref shape, [21], ComponentPropertyValue.FromInteger(7), true);
                    Write(failures, access, ref shape, [22], ComponentPropertyValue.FromInteger(1), false);
                    Write(failures, access, ref shape, [24], ComponentPropertyValue.FromInteger(1), false);
                    Write(failures, access, ref shape, [25], ComponentPropertyValue.FromInteger(10), true);
                    Write(failures, access, ref shape, [26], ComponentPropertyValue.FromInteger(11), true);

                    Check(failures, shape.Small == 5 && shape.Marks == (Marks.Left | Marks.Right) && shape.Note is null, "Small, Marks, Note");
                    Check(failures, shape.Tint.PackedValue == 0xFF00FF00 && shape.Owner.Value == 42 && shape.Condition.IsNone, "Tint, Owner, Condition");
                    Check(failures, shape.At.X == 8 && shape.Maybe is null && shape.MaybeMode == Mode.First, "At, Maybe, MaybeMode");
                    Check(failures, shape.Initialized == 7 && shape.Sentinel == 10 && shape.Field == 11, "Initialized, Sentinel, Field");

                    Restore(failures, access, ref shape, [20], ComponentPropertyValue.FromInteger(3), true);
                    Restore(failures, access, ref shape, [22], ComponentPropertyValue.FromInteger(4), true);
                    Restore(failures, access, ref shape, [14], ComponentPropertyValue.FromTagQuery(new GameplayTagQuery()), true);
                    Restore(failures, access, ref shape, [19], ComponentPropertyValue.FromDefinition(new DerivedDefinition()), true);
                    Restore(failures, access, ref shape, [16, 1], ComponentPropertyValue.FromInteger(6), true);
                    Restore(failures, access, ref shape, [24], ComponentPropertyValue.FromInteger(1), false);
                    Restore(failures, access, ref shape, [18, 0], ComponentPropertyValue.FromInteger(1), false);
                    Restore(failures, access, ref shape, [0], ComponentPropertyValue.FromInteger(200), false);
                    Check(failures, shape.Fixed == 3 && shape.Locked == 4 && shape.Accepts is not null && shape.Shared is not null && shape.Maybe == new Point(0, 6), "a restore writes read-only members, a tag query, a definition and a value under an absent optional");

                    var whole = new WrittenWhole(new Point(1, 1), 2, Marks.None);
                    var wholeAccess = ComponentPropertyAccessOf<WrittenWhole>.Access!;
                    Check(failures, wholeAccess.Write(ref whole, [1], ComponentPropertyValue.FromInteger(9)) && whole.Count == 9, "a record struct written through with");
                    Check(failures, wholeAccess.Write(ref whole, [0, 1], ComponentPropertyValue.FromInteger(5)) && whole.At == new Point(1, 5), "a nested record struct written through its holder");
                    return failures.ToArray();
                }

                private static void Write(List<string> failures, ComponentPropertyAccess<EveryShape> access, ref EveryShape shape, int[] path, ComponentPropertyValue value, bool expected)
                {
                    if (access.Write(ref shape, path, value) != expected)
                    {
                        failures.Add($"writing {value} at {string.Join(".", path)} should {(expected ? "succeed" : "be refused")}");
                    }
                }

                private static void Restore(List<string> failures, ComponentPropertyAccess<EveryShape> access, ref EveryShape shape, int[] path, ComponentPropertyValue value, bool expected)
                {
                    if (access.Restore(ref shape, path, value) != expected)
                    {
                        failures.Add($"restoring {value} at {string.Join(".", path)} should {(expected ? "succeed" : "be refused")}");
                    }
                }

                private static void Expect(List<string> failures, string text, string fragment)
                {
                    if (!text.Contains(fragment, StringComparison.Ordinal))
                    {
                        failures.Add($"read '{text}' lacks '{fragment}'");
                    }
                }

                private static void Check(List<string> failures, bool holds, string what)
                {
                    if (!holds)
                    {
                        failures.Add(what);
                    }
                }
            }

            public sealed class TextVisitor : ComponentPropertyVisitor
            {
                public string Output { get; private set; } = "";

                public override void Integer(ComponentPropertyDescriptor property, long value) => Add(property, value.ToString());
                public override void Number(ComponentPropertyDescriptor property, double value) => Add(property, value.ToString());
                public override void Boolean(ComponentPropertyDescriptor property, bool value) => Add(property, value.ToString());
                public override void Text(ComponentPropertyDescriptor property, string? value) => Add(property, value ?? "null");
                public override void Enum(ComponentPropertyDescriptor property, long value) => Add(property, value.ToString());
                public override void Guid(ComponentPropertyDescriptor property, Guid value) => Add(property, "guid");
                public override void Color(ComponentPropertyDescriptor property, uint packedValue) => Add(property, packedValue.ToString());
                public override void Tag(ComponentPropertyDescriptor property, GameplayTag value) => Add(property, value.IsNone ? "none" : value.Name);
                public override void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value) => Add(property, value is null ? "no query" : "query");
                public override void Definition(ComponentPropertyDescriptor property, object? value) => Add(property, value is null ? "definition null" : "definition");
                public override void Absent(ComponentPropertyDescriptor property) => Add(property, "absent");
                public override void BeginNested(ComponentPropertyDescriptor property) => Output += property.Name + "{";
                public override void EndNested(ComponentPropertyDescriptor property) => Output += "}";
                public override void BeginIndexed(ComponentPropertyDescriptor property, int count) => Output += property.Name + "[";
                public override void EndIndexed(ComponentPropertyDescriptor property) => Output += "]";

                private void Add(ComponentPropertyDescriptor property, string value) => Output += $"{property.Name}={value};";
            }
        }
        """;

    [TestMethod]
    public void EveryShape_CompilesRegistersReadsAndWritesAsDescribed()
    {
        var (compilation, diagnostics) = RunGenerator(EveryShape);
        Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));

        var errors = compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));

        var failures = RunProbe(compilation);
        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void EveryShape_GeneratesTheCheckedInSnapshot()
    {
        var (compilation, _) = RunGenerator(EveryShape);
        var generated = compilation.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("Sample.EveryShape.PropertyTable.g.cs", StringComparison.Ordinal)).ToString();
        var snapshotPath = Path.Combine(FindRepositoryRoot(), "Analyzers.Tests", "Snapshots", "EveryShape.PropertyTable.g.cs.txt");
        var expected = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath) : "";

        if (Normalize(expected) != Normalize(generated))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            File.WriteAllText(snapshotPath, generated);
            Assert.Fail($"The generated table changed and the snapshot was rewritten at {snapshotPath}: review the diff, then rerun.");
        }
    }

    [TestMethod]
    [DataRow("public Undescribed Inner { get; set; }", "HoldsOne.Inner is a Sample.Undescribed", DisplayName = "An undescribed struct")]
    [DataRow("public System.Collections.Generic.List<int> Items { get; set; }", "HoldsOne.Items is a System.Collections.Generic.List<int>", DisplayName = "An undescribed class")]
    [DataRow("private int _hidden; public int Shown { get; set; }", "HoldsOne._hidden is private state no described property reads", DisplayName = "Private state nothing reads")]
    [DataRow("public int Fixed { get; }", "HoldsOne.Fixed can be set only by a constructor", DisplayName = "A get-only auto-property")]
    public void AnUndescribableMember_ReportsCad0002NamingIt(string members, string expectedMessage)
    {
        var diagnostics = Diagnose($$"""
            using Engine.ECS.Properties;

            namespace Sample;

            public struct Undescribed
            {
                public int Value { get; set; }
            }

            [DescribedComponent]
            public partial struct HoldsOne
            {
                public int Count { get; set; }

                {{members}}
            }
            """);

        Assert.HasCount(1, diagnostics, string.Join(Environment.NewLine, diagnostics));
        Assert.AreEqual(ComponentPropertyTableGenerator.UndescribableId, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), expectedMessage);
        var lineSpan = diagnostics[0].Location.GetLineSpan();
        Assert.AreEqual("Sample.cs", lineSpan.Path, "The diagnostic points at the member's file.");
        Assert.IsGreaterThan(10, lineSpan.StartLinePosition.Line, "The diagnostic points at the member's line, not the struct's.");
    }

    [TestMethod]
    [DataRow("[NotAProperty] private int _cache;", DisplayName = "Private state marked NotAProperty")]
    [DataRow("private int _backing; public int Value { readonly get => _backing; set => _backing = value; }", DisplayName = "Private state a described property reads")]
    [DataRow("[ComponentProperty] private int _opted;", DisplayName = "Private state brought in")]
    [DataRow("[NotAProperty] public System.Collections.Generic.List<int> Items { get; set; }", DisplayName = "An undescribable member left out")]
    public void PrivateStateAccountedFor_ReportsNothing(string members)
    {
        var diagnostics = Diagnose($$"""
            using Engine.ECS.Properties;

            namespace Sample;

            [DescribedComponent]
            public partial struct HoldsOne
            {
                {{members}}
            }
            """);

        Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));
    }

    [TestMethod]
    [DataRow("[DescribedComponent] public struct NotPartial { public int Value { get; set; } }", "NotPartial must be partial", DisplayName = "Not partial")]
    [DataRow("public class Outer { [DescribedComponent] public partial struct Inner { public int Value { get; set; } } }", "Inner is nested in Outer, which must be partial", DisplayName = "Nested in a non-partial type")]
    [DataRow("[DescribedComponent] public partial struct Generic<T> { public int Value { get; set; } }", "Generic is generic", DisplayName = "Generic")]
    public void AStructThatCantTakeItsTable_ReportsCad0003(string declaration, string expectedMessage)
    {
        var diagnostics = Diagnose($"using Engine.ECS.Properties;\n\nnamespace Sample;\n\n{declaration}");

        Assert.HasCount(1, diagnostics, string.Join(Environment.NewLine, diagnostics));
        Assert.AreEqual(ComponentPropertyTableGenerator.NotPartialId, diagnostics[0].Id);
        StringAssert.Contains(diagnostics[0].GetMessage(), expectedMessage);
    }

    [TestMethod]
    public void AStructNestedInPartialTypes_IsDescribedInPlace()
    {
        var (compilation, diagnostics) = RunGenerator("""
            using Engine.ECS.Properties;

            namespace Sample;

            public static partial class Outer
            {
                [DescribedComponent]
                public partial struct Inner
                {
                    public int Value { get; set; }
                }
            }
            """);

        Assert.IsEmpty(diagnostics, string.Join(Environment.NewLine, diagnostics));
        Assert.IsEmpty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList());
    }

    private static ImmutableArray<Diagnostic> Diagnose(string source) => RunGenerator(source).Diagnostics;

    private static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorSample" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), path: "Sample.cs")],
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        CSharpGeneratorDriver.Create(new ComponentPropertyTableGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return (output, diagnostics);
    }

    private static string[] RunProbe(Compilation compilation)
    {
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.IsTrue(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("GeneratorProbe", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var failures = assembly.GetType("Sample.Probe", throwOnError: true)!
                .GetMethod("Failures", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, null);
            return (string[])failures!;
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DungeonCrawlerWorld.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
