using System.Text.RegularExpressions;

namespace Tests.Architecture;

/// <summary>Tests wait on what they wait for, not on time, and read only checked-in files and their own temp folders.</summary>
/// <remarks>An allowance is an exact count, so a new use in an allowed file fails, and so does one left listed after its use is gone.</remarks>
[TestClass]
public sealed partial class TestIsolationArchitectureTests
{
    private const string ThisFile = "Tests/Architecture/TestIsolationArchitectureTests.cs";

    private static readonly string[] TestProjectFolders = ["Tests", "Workshop.Tests", "Analyzers.Tests", "Tests.Support"];

    private static readonly Dictionary<string, (int Count, string Reason)> AllowedSleeps = new()
    {
        ["Tests/EngineTests/Diagnostics/FrameCostNestingTests.cs"] = (6, "Frame cost windows close on real time."),
        ["Tests/EngineTests/Diagnostics/StartupProfilerTests.cs"] = (2, "Startup windows close on real time."),
    };

    private static readonly Dictionary<string, (int Count, string Reason)> AllowedMachinePaths = new()
    {
        ["Tests/Architecture/RepositoryProjects.cs"] = (1, "Skips the Log folder when listing project files."),
        ["Tests/GameTests/Bootstrap/ModContentTests.cs"] = (3, "Checks where the player folders are, reading nothing in them."),
    };

    [GeneratedRegex(@"\bThread\.Sleep\b")]
    private static partial Regex ThreadSleep();

    [GeneratedRegex(@"""Log""|""Log[/\\]|\bLocalApplicationData\b|\bPlayerFolders\b")]
    private static partial Regex MachinePath();

    [GeneratedRegex(@"(?<![:""])//.*$", RegexOptions.Multiline)]
    private static partial Regex LineComment();

    [TestMethod]
    public void Tests_WaitOnEventsNotSleeps() =>
        AssertUsesMatchAllowances(ThreadSleep(), AllowedSleeps, "Thread.Sleep (await the event, or use TestWait.UntilAsync)");

    [TestMethod]
    public void Tests_ReadNoMachineState() =>
        AssertUsesMatchAllowances(MachinePath(), AllowedMachinePaths, "a path into Log or the player's folders (use a checked-in fixture or a temp folder)");

    private static void AssertUsesMatchAllowances(Regex use, Dictionary<string, (int Count, string Reason)> allowances, string description)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>();
        foreach (var (relativePath, source) in TestSources())
        {
            var count = use.Count(LineComment().Replace(source, string.Empty));
            if (count == 0)
            {
                continue;
            }

            seen.Add(relativePath);
            var allowed = allowances.TryGetValue(relativePath, out var allowance) ? allowance.Count : 0;
            if (count != allowed)
            {
                problems.Add($"{relativePath}: {count} use(s), {allowed} allowed");
            }
        }

        problems.AddRange(allowances.Keys.Where(path => !seen.Contains(path)).Select(static path => $"{path}: allowed but no longer used"));
        Assert.IsEmpty(problems, $"Each file's uses of {description} must match its allowance:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    private static IEnumerable<(string RelativePath, string Source)> TestSources()
    {
        foreach (var folder in TestProjectFolders)
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(TestRepository.Root, folder), "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(TestRepository.Root, path).Replace(Path.DirectorySeparatorChar, '/');
                if (relativePath != ThisFile && !relativePath.Split('/').Any(static segment => segment is "bin" or "obj" or "TestResults"))
                {
                    yield return (relativePath, File.ReadAllText(path));
                }
            }
        }
    }
}
