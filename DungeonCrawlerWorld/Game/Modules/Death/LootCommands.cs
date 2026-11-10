using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Currency;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles.Components;
using Game.World;

namespace Game.Modules.Death;

/// <summary>A session's looting, for code outside a system.</summary>
public sealed class LootCommands(ComponentManager componentManager, ItemCatalog itemCatalog, IPlayerQuery playerQuery)
{
    private readonly PackedComponentPool<ItemPileComponent> _piles = componentManager.GetPackedPool<ItemPileComponent>();

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

    /// <summary>Moves everything lootedEntityId holds -- every item stack, merging into the looter's interchangeable ones, and all currency -- to looterEntityId, marking it looted.</summary>
    /// <remarks>The one Take All: a loot window's option and the map's on a chest or pile both call it. The caller has checked the looter may reach it (ContainerAccess). What doesn't fit stays.</remarks>
    public void TakeAll(int lootedEntityId, int looterEntityId)
    {
        MarkLooted(lootedEntityId);
        InventoryActions.LootEveryStack(componentManager, itemCatalog, lootedEntityId, looterEntityId, playerQuery);
        CurrencyActions.TryTransferAll(componentManager, lootedEntityId, looterEntityId);
    }
}
