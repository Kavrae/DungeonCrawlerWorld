using Engine.Tags;
using Game.Blueprints;
using Game.Modules;
using Game.Modules.Achievements;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Modules.StatusEffects;
using Game.Terrain;

namespace Game.Bootstrap;

/// <summary>Every definition catalog a game session's modules filled during Configure.</summary>
/// <remarks>The context's own instances, not copies: a definition registered later through the context is visible here too.</remarks>
public sealed class GameCatalogs(GameModuleContext context)
{
    public ActionCatalog ActionCatalog { get; } = context.Actions;

    public ItemCatalog ItemCatalog { get; } = context.Items;

    public LootboxCatalog LootboxCatalog { get; } = context.Lootboxes;

    public AchievementCatalog AchievementCatalog { get; } = context.Achievements;

    public StatusEffectDisplayRegistry StatusEffectDisplays { get; } = context.StatusEffectDisplays;

    public TerrainRegistry Terrain { get; } = context.Terrain;

    /// <summary>Every gameplay tag this session's modules declared, and each one's display name.</summary>
    public GameplayTagRegistry GameplayTags { get; } = context.GameplayTags;

    /// <summary>Every blueprint definition, by session-local id and by Guid.</summary>
    public BlueprintRegistry Definitions { get; } = context.Definitions;
}
