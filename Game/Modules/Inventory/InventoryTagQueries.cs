using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Inventory;

/// <summary>Tag-frequency queries over an entity's inventory -- backs the Inventory window's dynamic per-tag tabs (see Presentation/UI/Inventory/InventoryManagementWindow.cs).</summary>
public static class InventoryTagQueries
{
    /// <summary>
    /// For every Tag carried by at least one of entityId's item stacks, counts how many distinct
    /// stacks carry it (a multi-tagged stack counts once toward each of its tags). Sorted by
    /// count descending, ties broken alphabetically by tag name for a stable tab order. A tag no
    /// current stack carries produces no entry at all -- tab generation is driven off this list
    /// directly, so an unused tag never gets a tab.
    /// </summary>
    public static List<(Tag Tag, int Count)> GetTagCounts(ComponentManager componentManager, ItemCatalog itemCatalog, int entityId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var reusableStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(stacks, entityId, reusableStacks);

        var counts = new Dictionary<Tag, int>();
        foreach (var stack in reusableStacks)
        {
            if (!itemCatalog.TryGet(stack.ItemDefinitionId, out var definition))
            {
                continue;
            }

            foreach (var tag in definition.Tags)
            {
                counts[tag] = counts.GetValueOrDefault(tag) + 1;
            }
        }

        var result = new List<(Tag Tag, int Count)>(counts.Count);
        foreach (var (tag, count) in counts)
        {
            result.Add((tag, count));
        }

        result.Sort(static (a, b) =>
        {
            var byCount = b.Count.CompareTo(a.Count);
            return byCount != 0 ? byCount : string.CompareOrdinal(a.Tag.ToString(), b.Tag.ToString());
        });

        return result;
    }
}
