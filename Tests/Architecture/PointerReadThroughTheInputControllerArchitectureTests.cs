using System.Text.RegularExpressions;

namespace Tests.Architecture;

/// <summary>
/// Only UiInputController reads the mouse (PLAN-unified-pointer-picking.md, Phase 2): every other piece of the UI asks
/// what it hit-tested -- Element.IsPointerOver, PointerState -- so nothing hovers through a window drawn over it, outside
/// its scrolled viewport or behind menu mode. Read from the source; comments may still name Mouse.GetState.
/// </summary>
[TestClass]
public sealed partial class PointerReadThroughTheInputControllerArchitectureTests
{
    private static readonly string[] UiSourceFolders =
    [
        Path.Combine("Cadence", "UIEngine"),
        Path.Combine("Cadence", "Workshop"),
        Path.Combine("DungeonCrawlerWorld", "Presentation"),
        Path.Combine("DungeonCrawlerWorld", "Workshop"),
        Path.Combine("DungeonCrawlerWorld", "GameShell"),
    ];

    private static readonly string InputControllerSource = Path.Combine("Cadence", "UIEngine", "Input", "UiInputController.cs");

    [TestMethod]
    public void UiCode_ReadsTheMouseOnlyInUiInputController()
    {
        var repositoryRoot = RepositoryProjects.FindRepositoryRoot();
        var violations = new List<string>();
        foreach (var folder in UiSourceFolders)
        {
            var folderPath = Path.Combine(repositoryRoot, folder);
            Assert.IsTrue(Directory.Exists(folderPath), $"{folderPath} is listed as UI source but doesn't exist.");

            foreach (var sourceFile in Directory.EnumerateFiles(folderPath, "*.cs", SearchOption.AllDirectories).Where(static path => !IsBuildOutput(path)))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, sourceFile);
                if (relativePath == InputControllerSource)
                {
                    continue;
                }

                var lines = File.ReadAllLines(sourceFile);
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    if (ReadsTheMouse(lines[lineIndex]))
                    {
                        violations.Add($"{relativePath}({lineIndex + 1}): {lines[lineIndex].Trim()}");
                    }
                }
            }
        }

        Assert.IsEmpty(violations, $"Read the pointer through Element.IsPointerOver or PointerState, not Mouse.GetState:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    /// <summary>The line check must catch code and pass comments, or the test above proves nothing.</summary>
    [TestMethod]
    public void LineCheck_CatchesCodeReadingTheMouse_AndPassesComments()
    {
        Assert.IsTrue(ReadsTheMouse("        UpdateHover(Mouse.GetState());"));
        Assert.IsTrue(ReadsTheMouse("        var state = Microsoft.Xna.Framework.Input.Mouse.GetState();"));
        Assert.IsFalse(ReadsTheMouse("    /// <summary>Never Mouse.GetState() here.</summary>"));
        Assert.IsFalse(ReadsTheMouse("        // Mouse.GetState() would ignore occlusion"));
        Assert.IsFalse(ReadsTheMouse("        var position = pointerState.CursorPosition;"));
    }

    private static bool ReadsTheMouse(string line) =>
        !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && MouseGetState().IsMatch(line);

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static segment => segment is "bin" or "obj");

    [GeneratedRegex(@"\bMouse\s*\.\s*GetState\s*\(")]
    private static partial Regex MouseGetState();
}
