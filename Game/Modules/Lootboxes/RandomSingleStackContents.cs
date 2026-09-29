using Engine.Math;
using Game.Modules.Inventory;

namespace Game.Modules.Lootboxes;

/// <summary>Placeholder loot box contents: one stack of 1-10 of a single item, picked uniformly from every tradeable item that isn't itself opened.</summary>
/// <remarks>
/// Every loot box uses this until TODO.md's "Lootbox drop tables" gives each type and rarity its own.
/// Candidates are ordered by Id before the pick, so the result depends only on the rolls and on which
/// items are registered, never on registration order.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed record RandomSingleStackContents : IItemContents
{
    public const ushort MinimumQuantity = 1;
    public const ushort MaximumQuantity = 10;

    public static RandomSingleStackContents Instance { get; } = new();

    public IReadOnlyList<ItemContentsEntry> Roll(SeededRandom rolls, ItemCatalog itemCatalog)
    {
        var candidateItemIds = new List<Guid>();
        foreach (var definition in itemCatalog.Definitions)
        {
            if (definition.Contents is null && definition.CanTrade && !definition.Tags.Contains(Tag.Lootbox))
            {
                candidateItemIds.Add(definition.Id);
            }
        }

        if (candidateItemIds.Count == 0)
        {
            return [];
        }

        candidateItemIds.Sort();

        var itemDefinitionId = candidateItemIds[rolls.Next(candidateItemIds.Count)];
        var quantity = (ushort)rolls.Next(MinimumQuantity, MaximumQuantity + 1);
        return [new ItemContentsEntry(itemDefinitionId, quantity)];
    }
}
