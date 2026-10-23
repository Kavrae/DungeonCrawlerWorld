using System.Text.RegularExpressions;

namespace Tests.Architecture;

/// <summary>
/// The UI draws through one pass, SpriteEffectBatch (PLAN-ui-on-the-sprite-effect.md): no code in UIEngine, Presentation or
/// either Workshop names SpriteBatch, so nothing begins a pass the batch doesn't know about or draws outlined text as nine
/// copies again. Read from the source, so a local, a cast or a `new` is caught as surely as a field or a parameter; comments
/// may still compare the batch with SpriteBatch.
/// </summary>
[TestClass]
public sealed partial class UiDrawsThroughTheSpriteEffectArchitectureTests
{
    /// <summary>The folders that draw the UI, relative to the repository root.</summary>
    private static readonly string[] UiSourceFolders =
    [
        Path.Combine("Cadence", "UIEngine"),
        Path.Combine("Cadence", "Workshop"),
        Path.Combine("DungeonCrawlerWorld", "Presentation"),
        Path.Combine("DungeonCrawlerWorld", "Workshop"),
    ];

    [TestMethod]
    public void UiCode_NamesNoSpriteBatch()
    {
        var repositoryRoot = RepositoryProjects.FindRepositoryRoot();
        var violations = new List<string>();
        foreach (var folder in UiSourceFolders)
        {
            var folderPath = Path.Combine(repositoryRoot, folder);
            Assert.IsTrue(Directory.Exists(folderPath), $"{folderPath} is listed as UI source but doesn't exist.");

            foreach (var sourceFile in Directory.EnumerateFiles(folderPath, "*.cs", SearchOption.AllDirectories).Where(static path => !IsBuildOutput(path)))
            {
                var lines = File.ReadAllLines(sourceFile);
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    if (NamesSpriteBatch(lines[lineIndex]))
                    {
                        violations.Add($"{Path.GetRelativePath(repositoryRoot, sourceFile)}({lineIndex + 1}): {lines[lineIndex].Trim()}");
                    }
                }
            }
        }

        Assert.IsEmpty(violations, $"UI code draws through UiRenderService.Batch (SpriteEffectBatch), not SpriteBatch:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    /// <summary>The line check must catch code and pass comments, or the test above proves nothing.</summary>
    [TestMethod]
    public void LineCheck_CatchesCodeNamingSpriteBatch_AndPassesComments()
    {
        Assert.IsTrue(NamesSpriteBatch("    private void Draw(SpriteBatch spriteBatch)"));
        Assert.IsTrue(NamesSpriteBatch("        var batch = new SpriteBatch(graphicsDevice);"));
        Assert.IsTrue(NamesSpriteBatch("        Microsoft.Xna.Framework.Graphics.SpriteBatch batch = null!;"));
        Assert.IsFalse(NamesSpriteBatch("    /// <summary>Draws where SpriteBatch would.</summary>"));
        Assert.IsFalse(NamesSpriteBatch("        // SpriteBatch's order"));
        Assert.IsFalse(NamesSpriteBatch("    private void Draw(SpriteEffectBatch batch)"));
    }

    /// <summary>Whether a line of code (not a comment line) names the type SpriteBatch.</summary>
    private static bool NamesSpriteBatch(string line) =>
        !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && SpriteBatchName().IsMatch(line);

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static segment => segment is "bin" or "obj");

    [GeneratedRegex(@"\bSpriteBatch\b")]
    private static partial Regex SpriteBatchName();
}
