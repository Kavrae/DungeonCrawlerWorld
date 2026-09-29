using Game.Modules.Inventory;

namespace Game.Modules.Lootboxes;

/// <summary>A type and rarity of loot box: which item a box is.</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct LootboxKind(Guid TypeId, LootboxRarity Rarity);

/// <summary>A loot box an award grants: its type, its rarity and, optionally, contents that replace what that type and rarity grant.</summary>
/// <param name="TypeId">The LootboxTypeDefinition's Id.</param>
/// <param name="Rarity">The box's rarity.</param>
/// <param name="Contents">What this box grants instead of its type and rarity's own contents; null for the usual contents.</param>
/// <remarks>A box with Contents stacks only with boxes carrying equal contents, never with the usual box of its type and rarity.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed record LootboxReward(Guid TypeId, LootboxRarity Rarity, IItemContents? Contents = null)
{
    public LootboxKind Kind => new(TypeId, Rarity);
}
