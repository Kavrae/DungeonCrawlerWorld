using Engine.Math;
using Game.Modules.Inventory;
using Game.Tags;

namespace Game.Modules.Lootboxes;

/// <summary>Placeholder loot box contents: one stack of 1-10 of a single item, picked uniformly from every tradeable item that isn't itself opened.</summary>
/// <remarks>
/// Every loot box uses this until TODO.md's "Lootbox drop tables" gives each type and rarity its own.
/// Candidates are read from the catalog's summaries (ItemCatalog.Summaries: every item the content holds, loaded or
/// not, and any registered beside it) and ordered by Id before the pick, so the result depends only on the rolls and on
/// which items exist, never on what has been loaded or in what order.
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
        foreach (var summary in itemCatalog.Summaries)
        {
            if (!summary.HasContents && summary.CanTrade && !summary.DeclaredTags.Has(GameTags.ItemLootbox))
            {
                candidateItemIds.Add(summary.Id);
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
