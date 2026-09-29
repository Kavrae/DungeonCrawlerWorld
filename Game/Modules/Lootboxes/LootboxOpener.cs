using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Game.Modules.Lootboxes;

/// <summary>One item an opening granted, summed across every box of a group.</summary>
/// <param name="ItemDefinitionId">The item granted.</param>
/// <param name="Quantity">How many the group's boxes granted in total.</param>
/// <param name="StackInstanceId">The stack the last one granted landed in.</param>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct GrantedItem(Guid ItemDefinitionId, int Quantity, uint StackInstanceId);

/// <summary>Every box of one type and rarity an opening opened, and everything they granted together.</summary>
/// <param name="Kind">The boxes' type and rarity.</param>
/// <param name="Count">How many boxes of this kind were opened.</param>
/// <param name="Items">What they granted, one entry per item in the order each was first granted.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record OpenedLootboxGroup(LootboxKind Kind, int Count, IReadOnlyList<GrantedItem> Items);

/// <summary>Opens every loot box an entity holds, granting their contents straight into its inventory.</summary>
/// <remarks>
/// A direct call, not a system: opening answers one click, needs no action lock or target, and touches
/// only the opener's own inventory, which is safe to change between frames. Boxes open grouped by type
/// and rarity -- every stack of one kind is one group, including boxes whose contents were overridden --
/// lowest rarity first, then by type name. The opener draws every roll from its own sequence, seeded
/// once from the session seed, so opening boxes never shifts another random choice and a seeded run
/// opens to the same items.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class LootboxOpener(ComponentManager componentManager, LootboxCatalog lootboxCatalog, ItemCatalog itemCatalog, ulong seed)
{
    private readonly SeededRandom _rolls = new(seed);

    /// <summary>Opens every loot box entityId holds and consumes them.</summary>
    /// <returns>One group per type and rarity opened, in opening order; empty when it held none.</returns>
    public IReadOnlyList<OpenedLootboxGroup> OpenAll(int entityId)
    {
        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        var heldStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(stacks, entityId, heldStacks);

        var boxStacksByKind = new Dictionary<LootboxKind, List<(InventoryItemStackComponent Stack, ItemDefinition Definition)>>();
        foreach (var stack in heldStacks)
        {
            if (!InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var definition) ||
                !definition.Tags.Contains(Tag.Lootbox) ||
                !lootboxCatalog.TryGetKind(stack.ItemDefinitionId, out var kind))
            {
                continue;
            }

            if (!boxStacksByKind.TryGetValue(kind, out var boxStacks))
            {
                boxStacks = [];
                boxStacksByKind.Add(kind, boxStacks);
            }

            boxStacks.Add((stack, definition));
        }

        var kindsInOpeningOrder = boxStacksByKind.Keys
            .OrderBy(static kind => kind.Rarity)
            .ThenBy(kind => lootboxCatalog.GetTypeDefinition(kind.TypeId).Name, StringComparer.Ordinal)
            .ToList();

        var openedGroups = new List<OpenedLootboxGroup>(kindsInOpeningOrder.Count);
        foreach (var kind in kindsInOpeningOrder)
        {
            openedGroups.Add(OpenGroup(entityId, kind, boxStacksByKind[kind]));
        }

        return openedGroups;
    }

    private OpenedLootboxGroup OpenGroup(int entityId, LootboxKind kind, List<(InventoryItemStackComponent Stack, ItemDefinition Definition)> boxStacks)
    {
        var grantedItems = new List<GrantedItem>();
        var grantedItemIndexById = new Dictionary<Guid, int>();
        var boxCount = 0;

        foreach (var (stack, definition) in boxStacks)
        {
            for (var unit = 0; unit < stack.Quantity; unit++)
            {
                InventoryActions.ConsumeItemByStackInstanceId(componentManager, entityId, stack.StackInstanceId);
                boxCount++;

                if (definition.Contents is not { } contents)
                {
                    continue;
                }

                foreach (var entry in contents.Roll(_rolls, itemCatalog))
                {
                    if (!itemCatalog.TryGet(entry.ItemDefinitionId, out var rewardDefinition))
                    {
                        continue;
                    }

                    var landedStackInstanceId = ItemGrants.Grant(componentManager, entityId, rewardDefinition, entry.Quantity);

                    if (grantedItemIndexById.TryGetValue(entry.ItemDefinitionId, out var index))
                    {
                        grantedItems[index] = grantedItems[index] with { Quantity = grantedItems[index].Quantity + entry.Quantity, StackInstanceId = landedStackInstanceId };
                    }
                    else
                    {
                        grantedItemIndexById.Add(entry.ItemDefinitionId, grantedItems.Count);
                        grantedItems.Add(new GrantedItem(entry.ItemDefinitionId, entry.Quantity, landedStackInstanceId));
                    }
                }
            }
        }

        return new OpenedLootboxGroup(kind, boxCount, grantedItems);
    }
}
