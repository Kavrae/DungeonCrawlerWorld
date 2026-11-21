using Engine.Tags;
using Game.Bootstrap;
using Game.Modules.Inventory;
using Game.Modules.StatusEffects;
using Game.Terrain;
using Game.Views;

namespace Presentation.UI;

/// <summary>Where effect text finds the names it shows: gameplay tags, terrain, how long a status effect's duration tick lasts, what caused a modifier, and items.</summary>
/// <remarks>Each is optional: without one, a line falls back to the raw name (a tag's name, a terrain's key), counts in ticks, or calls a modifier's cause "Modifier".</remarks>
public sealed record EffectTextNames(GameplayTagRegistry? Tags = null, TerrainRegistry? Terrain = null, StatusEffectDisplayRegistry? StatusEffects = null, ActionSourceNaming? Sources = null, ItemCatalog? Items = null)
{
    public static readonly EffectTextNames None = new();

    /// <summary>Every name a session's catalogs hold, and its views' names for what caused an effect.</summary>
    public static EffectTextNames Of(GameCatalogs catalogs, ActionSourceNaming? sources = null) => new(catalogs.GameplayTags, catalogs.Terrain, catalogs.StatusEffectDisplays, sources, catalogs.ItemCatalog);

    /// <summary>tag's display name, or its name without a registry.</summary>
    public string DisplayNameOf(GameplayTag tag) => Tags?.GetDisplayName(tag) ?? tag.LastSegment;

    /// <summary>The name of the item itemId, or its Guid when there is no catalog or it holds no such item.</summary>
    public string ItemNameOf(Guid itemId) => Items is not null && Items.TryGet(itemId, out var item) ? item.Name : itemId.ToString();

    /// <summary>What tag means, or its name without a registry.</summary>
    public string DescriptionOf(GameplayTag tag) => Tags?.GetDescription(tag) ?? tag.Name;

    /// <summary>tag's plural display name, or its name without a registry.</summary>
    public string PluralDisplayNameOf(GameplayTag tag) => Tags?.GetPluralDisplayName(tag) ?? tag.LastSegment;

    /// <summary>The name of the terrain registered under key, or key itself when there is none.</summary>
    public string TerrainNameOf(string key) =>
        Terrain is not null && Terrain.TryGetId(key, out var typeId) && Terrain.TryGet(typeId, out var definition) ? definition.Name : key;

    /// <summary>How long one duration tick of effectType lasts, in frames, or null when it isn't known.</summary>
    public ushort? FramesPerDurationTickOf(StatusEffectType effectType) =>
        StatusEffects is not null && StatusEffects.TryGet(effectType, out var display) ? display.FramesPerDurationTick : null;
}
