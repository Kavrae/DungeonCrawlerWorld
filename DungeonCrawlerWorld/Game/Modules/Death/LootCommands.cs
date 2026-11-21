using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Containers.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles.Components;
using Game.World;

namespace Game.Modules.Death;

/// <summary>A session's looting, for code outside a system.</summary>
public sealed class LootCommands(ComponentManager componentManager, ItemCatalog itemCatalog, IPlayerQuery playerQuery)
{
    private readonly PackedComponentPool<ItemPileComponent> _piles = componentManager.GetPackedPool<ItemPileComponent>();
    private readonly PackedComponentPool<ContainerComponent> _containers = componentManager.GetPackedPool<ContainerComponent>();
    private readonly PackedComponentPool<CurrencyComponent> _currencies = componentManager.GetPackedPool<CurrencyComponent>();

    /// <summary>Records that entityId's loot has been opened at least once, whether or not anything was taken.</summary>
    /// <remarks>An item pile is never marked: it has no opened state worth showing, so its bag always reads as unlooted.</remarks>
    public void MarkLooted(int entityId)
    {
        if (_piles.Has(entityId))
        {
            return;
        }

        componentManager.Merge(entityId, new LootedComponent());
    }

    /// <summary>Whether entityId is a chest, an item pile or a shop, standing or destroyed, rather than a corpse.</summary>
    public bool IsContainer(int entityId) => _containers.Has(entityId);

    /// <summary>Whether TakeAll would move anything: some currency, or a stack that can be traded and has room to go.</summary>
    public bool CanTakeAll(int lootedEntityId, int looterEntityId) =>
        lootedEntityId != looterEntityId &&
        ((_currencies.TryGetReadonly(lootedEntityId, out var currency) && (currency.Gold > 0 || currency.Credits > 0)) ||
         InventoryActions.CanLootAny(componentManager, itemCatalog, lootedEntityId, looterEntityId, playerQuery));

    /// <summary>Moves everything lootedEntityId holds -- every item stack, merging into the looter's interchangeable ones, and all currency -- to looterEntityId, marking it looted.</summary>
    /// <remarks>The one Take All: a loot window's option and the map's on a chest or pile both call it. The caller has checked the looter may reach it (ContainerAccess). What doesn't fit stays.</remarks>
    public void TakeAll(int lootedEntityId, int looterEntityId)
    {
        MarkLooted(lootedEntityId);
        InventoryActions.LootEveryStack(componentManager, itemCatalog, lootedEntityId, looterEntityId, playerQuery);
        CurrencyActions.TryTransferAll(componentManager, lootedEntityId, looterEntityId);
    }
}
