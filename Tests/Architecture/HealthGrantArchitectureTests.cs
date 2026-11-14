namespace Tests.Architecture;

/// <summary>
/// A blueprint gives an entity simple health through MaximumHealthShift.MergeBaseMaximum, never by building a
/// SimpleHealthComponent itself: a merge has no entity to read MaximumHealth modifiers for, so only the helper moves
/// current health by as far as the modified maximum travelled. Read from the source of the game's blueprints.
/// </summary>
[TestClass]
public sealed class HealthGrantArchitectureTests
{
    private static readonly string BlueprintsFolder = Path.Combine("DungeonCrawlerWorld", "Game", "Blueprints");

    [TestMethod]
    public void Blueprints_NeverBuildASimpleHealthComponent()
    {
        var repositoryRoot = RepositoryProjects.FindRepositoryRoot();
        var violations = new List<string>();
        foreach (var sourceFile in Directory.EnumerateFiles(Path.Combine(repositoryRoot, BlueprintsFolder), "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(sourceFile);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                if (BuildsASimpleHealthComponent(lines[lineIndex]))
                {
                    violations.Add($"{Path.GetRelativePath(repositoryRoot, sourceFile)}({lineIndex + 1}): {lines[lineIndex].Trim()}");
                }
            }
        }

        Assert.IsEmpty(violations, $"A blueprint grants simple health through MaximumHealthShift.MergeBaseMaximum, which applies the entity's MaximumHealth modifiers on top of the merged base:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [TestMethod]
    public void LineCheck_CatchesABuiltComponent_AndPassesTheHelperAndComments()
    {
        Assert.IsTrue(BuildsASimpleHealthComponent("        componentManager.Merge(entityId, new SimpleHealthComponent(MaximumHealth, MaximumHealth));"));
        Assert.IsTrue(BuildsASimpleHealthComponent("        health.Add(entityId, new SimpleHealthComponent(10, 10));"));
        Assert.IsFalse(BuildsASimpleHealthComponent("        MaximumHealthShift.MergeBaseMaximum(componentManager, entityId, MaximumHealth);"));
        Assert.IsFalse(BuildsASimpleHealthComponent("        if (!componentManager.GetPackedPool<SimpleHealthComponent>().Has(entityId))"));
        Assert.IsFalse(BuildsASimpleHealthComponent("    /// <summary>Unlike new SimpleHealthComponent(...), ...</summary>"));
    }

    private static bool BuildsASimpleHealthComponent(string line) =>
        !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && line.Contains("new SimpleHealthComponent(", StringComparison.Ordinal);
}
