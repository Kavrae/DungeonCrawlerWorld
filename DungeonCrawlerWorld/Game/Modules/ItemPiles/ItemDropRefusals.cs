using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;

namespace Game.Modules.ItemPiles;

/// <summary>Why an entity can't drop something now, wherever it would land.</summary>
/// <remarks>Where it lands is ItemDrops' to check. Read by ItemDrops before every drop, and by ActionStateView for the menus and the armed drop.</remarks>
public sealed class ItemDropRefusals(ComponentManager componentManager, ItemCatalog itemCatalog)
{
    private readonly PackedComponentPool<DeadComponent> _deadEntities = componentManager.GetPackedPool<DeadComponent>();
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly PackedComponentPool<CurrencyComponent> _currencies = componentManager.GetPackedPool<CurrencyComponent>();
    private readonly PackedComponentPool<PendingWindupComponent> _pendingWindups = componentManager.GetPackedPool<PendingWindupComponent>();
    private readonly PackedComponentPool<PendingItemActivationComponent> _pendingItemActivations = componentManager.GetPackedPool<PendingItemActivationComponent>();

    /// <summary>Why dropperEntityId can't drop drop now, or None.</summary>
    public ItemDropRefusal GetRefusal(int dropperEntityId, in ItemDrop drop)
    {
        if (_deadEntities.Has(dropperEntityId))
        {
            return ItemDropRefusal.DropperDead;
        }

        return drop.Kind switch
        {
            ItemDropKind.OneUnit or ItemDropKind.WholeStack => RefusalForStack(dropperEntityId, drop.StackInstanceId),
            ItemDropKind.EveryStackOfItem => RefusalForEveryStackOf(dropperEntityId, drop.ItemDefinitionId),
            _ => HoldsCurrency(dropperEntityId, in drop) ? ItemDropRefusal.None : ItemDropRefusal.NothingToDrop,
        };
    }

    private ItemDropRefusal RefusalForStack(int dropperEntityId, uint stackInstanceId) =>
        InventoryQueries.TryFindByStackInstanceId(_inventoryStacks, dropperEntityId, stackInstanceId, out var stack)
            ? RefusalFor(dropperEntityId, in stack)
            : ItemDropRefusal.NothingToDrop;

    private ItemDropRefusal RefusalForEveryStackOf(int dropperEntityId, Guid itemDefinitionId)
    {
        var refusal = ItemDropRefusal.NothingToDrop;
        for (var denseIndex = _inventoryStacks.GetFirstDenseIndex(dropperEntityId); denseIndex != -1; denseIndex = _inventoryStacks.GetNextDenseIndex(denseIndex))
        {
            ref readonly var stack = ref _inventoryStacks.GetReadonlyByDenseIndex(denseIndex);
            if (stack.ItemDefinitionId != itemDefinitionId)
            {
                continue;
            }

            refusal = RefusalFor(dropperEntityId, in stack);
            if (refusal != ItemDropRefusal.None)
            {
                return refusal;
            }
        }

        return refusal;
    }

    private ItemDropRefusal RefusalFor(int dropperEntityId, in InventoryItemStackComponent stack)
    {
        if (InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) && !item.CanTrade)
        {
            return ItemDropRefusal.CannotTrade;
        }

        return IsStackInUse(dropperEntityId, stack.StackInstanceId) ? ItemDropRefusal.StackInUse : ItemDropRefusal.None;
    }

    private bool HoldsCurrency(int entityId, in ItemDrop drop)
    {
        if (!_currencies.TryGetReadonly(entityId, out var currency))
        {
            return false;
        }

        return drop.Kind == ItemDropKind.AllCurrency
            ? currency.Gold > 0 || currency.Credits > 0
            : (drop.CurrencyType == CurrencyType.Gold ? currency.Gold : currency.Credits) > 0;
    }

    private bool IsStackInUse(int entityId, uint stackInstanceId) =>
        (_pendingWindups.TryGetReadonly(entityId, out var windup) && windup.Activatable == ActivatableReference.ItemStack(stackInstanceId)) ||
        (_pendingItemActivations.TryGetReadonly(entityId, out var activation) && activation.StackInstanceId == stackInstanceId);
}
