using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Tags;
using Game.Modules.Inventory.Components;
using Game.Tags;

namespace Game.Modules.Inventory;

/// <summary>Item-category queries over an entity's inventory -- backs the Inventory window's per-category tabs (see Presentation/UI/Inventory/InventoryManagementWindow.cs).</summary>
public static class InventoryTagQueries
{
    /// <summary>For every item category entityId's stacks fall in, how many distinct stacks are in it, most populated first.</summary>
    /// <remarks>
    /// A category is an Item.* tag. Each stack counts once under each of its Item.* tags and under every
    /// ancestor of those below Item itself -- a Health Potion (Item.Consumable.Potion) counts under Potion and
    /// Consumable. Every other tag (Self, Healing, Fire, Magic) is not a category. Ties are broken by display
    /// name, then full name, for a stable tab order. A category no stack falls in produces no entry, so it never
    /// gets a tab.
    /// </remarks>
    public static List<(GameplayTag Tag, int Count)> GetItemCategoryCounts(ComponentManager componentManager, ItemCatalog itemCatalog, GameplayTagRegistry gameplayTags, int entityId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var reusableStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(stacks, entityId, reusableStacks);

        var counts = new Dictionary<GameplayTag, int>();
        var stackCategories = new HashSet<GameplayTag>();
        foreach (var stack in reusableStacks)
        {
            if (!itemCatalog.TryGet(stack.ItemDefinitionId, out var definition))
            {
                continue;
            }

            stackCategories.Clear();
            foreach (var tag in definition.Tags)
            {
                for (var category = tag; category.IsSelfOrDescendantOf(GameTags.Item) && category != GameTags.Item; category = category.Parent)
                {
                    stackCategories.Add(category);
                }
            }

            foreach (var category in stackCategories)
            {
                counts[category] = counts.GetValueOrDefault(category) + 1;
            }
        }

        var result = new List<(GameplayTag Tag, int Count)>(counts.Count);
        foreach (var (tag, count) in counts)
        {
            result.Add((tag, count));
        }

        result.Sort((a, b) =>
        {
            var byCount = b.Count.CompareTo(a.Count);
            if (byCount != 0)
            {
                return byCount;
            }

            var byDisplayName = string.CompareOrdinal(gameplayTags.GetDisplayName(a.Tag), gameplayTags.GetDisplayName(b.Tag));
            return byDisplayName != 0 ? byDisplayName : string.CompareOrdinal(a.Tag.Name, b.Tag.Name);
        });

        return result;
    }
}
