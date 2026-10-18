using Builders.Browsing;
using Builders.References;
using Engine.Tags;
using Game.Modules.Auras;

namespace Builders.Auras;

/// <summary>The Auras browser's groups: by what puts the aura in the world, the most permanent first.</summary>
/// <remarks>Terrain radiates its aura everywhere it lies, a blueprint's (a fixture's) wherever one is spawned, and an item's or action's only while used; an aura with several sources goes under the first of these it has, and one nothing names under "Unused".</remarks>
public static class AuraBrowserSections
{
    public const string TerrainHeading = "Terrain";
    public const string FixturesHeading = "Fixtures";
    public const string ItemsAndActionsHeading = "Items and actions";
    public const string UnusedHeading = "Unused";

    public static IReadOnlyList<string> HeadingsInOrder { get; } = [TerrainHeading, FixturesHeading, ItemsAndActionsHeading, UnusedHeading];

    public static IReadOnlyList<RecordBrowserSection<AuraDefinition>> Build(IEnumerable<AuraDefinition> auras, ContentReferenceIndex references, string search, GameplayTagRegistry gameplayTags) =>
        RecordBrowserSections.Build(auras, HeadingsInOrder, aura => HeadingOf(aura, references), static aura => aura.Name, static aura => aura.Tags, search, gameplayTags);

    private static string HeadingOf(AuraDefinition aura, ContentReferenceIndex references)
    {
        var usedBy = references.UsedBy(ContentRecordKind.Aura, aura.Id);
        if (usedBy.Any(reference => reference.From.Kind == ContentRecordKind.Terrain))
        {
            return TerrainHeading;
        }

        if (usedBy.Any(reference => reference.From.Kind == ContentRecordKind.Blueprint))
        {
            return FixturesHeading;
        }

        return usedBy.Count > 0 ? ItemsAndActionsHeading : UnusedHeading;
    }
}
