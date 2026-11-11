using System.Text.RegularExpressions;

namespace Tests.Architecture;

/// <summary>
/// A game loads its actions and items by id (PLAN-data-driven-actions-and-items.md Phase 5), so what an ActionCatalog or ItemCatalog
/// holds is whatever happens to have been asked for: game code answers a whole-catalog question from the catalog's Summaries,
/// never by enumerating Definitions. Read from the source of the game's own projects; the tools (the Workshop, the exporter)
/// build eagerly and may enumerate.
/// </summary>
[TestClass]
public sealed partial class WholeCatalogReadsArchitectureTests
{
    private static readonly string[] GameSourceFolders =
    [
        Path.Combine("DungeonCrawlerWorld", "Game"),
        Path.Combine("DungeonCrawlerWorld", "Presentation"),
        Path.Combine("DungeonCrawlerWorld", "GameShell"),
    ];

    /// <summary>The whole-build content checks: they run in every build over what it registered, which in a trial build (ModValidation) is every record.</summary>
    private static readonly string[] AllowedFiles =
    [
        Path.Combine("DungeonCrawlerWorld", "Game", "Effects", "EffectContent.cs"),
        Path.Combine("DungeonCrawlerWorld", "Game", "Tags", "ContentTagValidation.cs"),
        Path.Combine("DungeonCrawlerWorld", "Game", "Modules", "Actions", "ToggleContentValidation.cs"),
        Path.Combine("DungeonCrawlerWorld", "Game", "Modules", "Actions", "TargetingContentValidation.cs"),
        Path.Combine("DungeonCrawlerWorld", "Game", "Modules", "Inventory", "ItemCooldownContentValidation.cs"),
    ];

    [TestMethod]
    public void GameCode_NeverEnumeratesAnActionOrItemCatalog()
    {
        var repositoryRoot = RepositoryProjects.FindRepositoryRoot();
        var violations = new List<string>();
        foreach (var folder in GameSourceFolders)
        {
            foreach (var sourceFile in Directory.EnumerateFiles(Path.Combine(repositoryRoot, folder), "*.cs", SearchOption.AllDirectories).Where(static path => !IsBuildOutput(path)))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, sourceFile);
                if (AllowedFiles.Contains(relativePath))
                {
                    continue;
                }

                var lines = File.ReadAllLines(sourceFile);
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    if (EnumeratesACatalog(lines[lineIndex]))
                    {
                        violations.Add($"{relativePath}({lineIndex + 1}): {lines[lineIndex].Trim()}");
                    }
                }
            }
        }

        Assert.IsEmpty(violations, $"Game code reads a whole catalog through ActionCatalog.Summaries or ItemCatalog.Summaries, which hold every record the content has, loaded or not:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [TestMethod]
    public void LineCheck_CatchesAnEnumeratedCatalog_AndPassesSummariesAndComments()
    {
        Assert.IsTrue(EnumeratesACatalog("        foreach (var item in itemCatalog.Definitions)"));
        Assert.IsTrue(EnumeratesACatalog("        var all = context.Actions.Definitions.ToList();"));
        Assert.IsTrue(EnumeratesACatalog("        catalogs.ItemCatalog.Definitions.Count()"));
        Assert.IsFalse(EnumeratesACatalog("        foreach (var item in itemCatalog.Summaries)"));
        Assert.IsFalse(EnumeratesACatalog("        var blueprints = context.Definitions;"));
        Assert.IsFalse(EnumeratesACatalog("using Game.Modules.Actions.Definitions;"));
        Assert.IsFalse(EnumeratesACatalog("    /// <summary>Unlike ItemCatalog.Definitions, ...</summary>"));
    }

    private static bool EnumeratesACatalog(string line) =>
        !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && CatalogDefinitions().IsMatch(line);

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(static segment => segment is "bin" or "obj");

    [GeneratedRegex(@"(?<!Modules\.)\b(?:[Aa]ctions|[Ii]tems|[Aa]ctionCatalog|[Ii]temCatalog)\.Definitions\b")]
    private static partial Regex CatalogDefinitions();
}
