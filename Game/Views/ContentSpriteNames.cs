using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Terrain;

namespace Game.Views;

/// <summary>A sprite name some content declares, and the first thing found declaring it, for a message that says where to look.</summary>
public readonly record struct DeclaredSpriteName(string SpriteName, string DeclaredBy);

/// <summary>Every sprite name the session's content declares: blueprints, terrain, actions, items and loot box types.</summary>
/// <remarks>
/// Game only names sprites; whether the manifest has a sprite by each name is Presentation's to check (and a missing one draws its
/// glyph). Read from the catalogs when asked, so content registered after the build is included. A blueprint's
/// AppearanceFacet.NoSprite is a deliberate "glyph only", not a name, and is left out.
/// </remarks>
public sealed class ContentSpriteNames(BlueprintRegistry blueprints, TerrainRegistry terrain, ActionCatalog actions, ItemCatalog items, LootboxCatalog lootboxes)
{
    /// <summary>Each declared sprite name once, with the first thing found declaring it.</summary>
    public IReadOnlyList<DeclaredSpriteName> Collect()
    {
        var declared = new List<DeclaredSpriteName>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? spriteName, string declaredBy)
        {
            if (!string.IsNullOrEmpty(spriteName) && seen.Add(spriteName))
            {
                declared.Add(new DeclaredSpriteName(spriteName, declaredBy));
            }
        }

        for (ushort id = 1; id <= blueprints.Count; id++)
        {
            if (blueprints.TryGet(id, out var definition))
            {
                Add(definition.Appearance?.SpriteName, $"blueprint {definition.Name}");
            }
        }

        for (ushort id = 1; id <= terrain.Count; id++)
        {
            if (terrain.TryGet(id, out var definition))
            {
                Add(definition.SpriteName, $"terrain {definition.Key}");
            }
        }

        foreach (var action in actions.Definitions)
        {
            Add(action.SpriteName, $"action {action.Name}");
        }

        foreach (var item in items.Definitions)
        {
            Add(item.SpriteName, $"item {item.Name}");
        }

        foreach (var lootboxType in lootboxes.Definitions)
        {
            foreach (var rarity in Enum.GetValues<LootboxRarity>())
            {
                Add(LootboxCatalog.SpriteNameFor(lootboxType, rarity), $"loot box type {lootboxType.Name}");
            }
        }

        return declared;
    }
}
