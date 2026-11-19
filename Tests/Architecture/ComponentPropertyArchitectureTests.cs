using System.Text.RegularExpressions;
using Engine.ECS.Properties;
using Engine.Math;
using Game.World;

namespace Tests.Architecture;

/// <summary>
/// Component property access (Engine.ECS.Properties) is for inspection, editing, saves and diagnostics, never gameplay: a raw
/// write raises no event and follows no rule. Its attributes may describe a component anywhere; its runtime types are read
/// only from the hand-written tables, admin, views and diagnostics. And every built-in pool has a table.
/// </summary>
[TestClass]
public sealed partial class ComponentPropertyArchitectureTests
{
    private static readonly string[] AllowedFolders =
    [
        Path.Combine("DungeonCrawlerWorld", "Game", "ComponentProperties"),
        Path.Combine("DungeonCrawlerWorld", "Game", "Admin"),
        Path.Combine("DungeonCrawlerWorld", "Game", "Views"),
        Path.Combine("DungeonCrawlerWorld", "Game", "Diagnostics"),
    ];

    [TestMethod]
    public void GameplayCode_NeverUsesComponentPropertyAccess()
    {
        var repositoryRoot = RepositoryProjects.FindRepositoryRoot();
        var gameRoot = Path.Combine(repositoryRoot, "DungeonCrawlerWorld", "Game");
        var violations = new List<string>();
        foreach (var sourceFile in Directory.EnumerateFiles(gameRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, sourceFile);
            if (IsBuildOutput(relativePath) || AllowedFolders.Any(folder => relativePath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                continue;
            }

            var lines = File.ReadAllLines(sourceFile);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (RuntimeTypeName().IsMatch(lines[lineIndex]))
                {
                    violations.Add($"{relativePath}({lineIndex + 1}): {lines[lineIndex].Trim()}");
                }
            }
        }

        Assert.IsEmpty(violations, $"Gameplay acts through effects, modifiers, commands and spawns, never a component property table:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [TestMethod]
    public void EveryBuiltInPool_HasAPropertyTable()
    {
        var pass = BuiltInTestModules.Build(new Map(new Vector3Int(8, 8, 3)));

        var undescribed = pass.EcsContext.ComponentManager.PoolsInRegistrationOrder
            .Where(pool => pool is not IDescribedComponentPool { PropertyTable: not null })
            .Select(pool => pool.ComponentType.Name)
            .ToList();

        Assert.IsEmpty(undescribed, $"Describe these components with [DescribedComponent] (or [DescribedByHand]): {string.Join(", ", undescribed)}");
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || relativePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [GeneratedRegex(@"\b(ComponentPropertyVisitor|ComponentPropertyAccess|ComponentPropertyAccessOf|ComponentPropertyValue|ComponentPropertyTable|ComponentPropertyDescriptor|IDescribedComponentPool)\b")]
    private static partial Regex RuntimeTypeName();
}
