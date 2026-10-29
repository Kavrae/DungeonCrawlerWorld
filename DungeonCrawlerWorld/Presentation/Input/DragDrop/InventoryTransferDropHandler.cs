using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Shops;
using Presentation.UI.Trade;
using UIEngine.Input;

namespace Presentation.Input.DragDrop;

/// <summary>An item or currency dropped on another inventory: a give, a loot, a buy or sell, or staging in a trade.</summary>
/// <remarks>
/// What the drop means depends on who the two entities are, and each feature owns that through its own IDragDropResolver,
/// tried in a fixed order until one claims it: Trade before Shop (a trade-offer entity must never reach ShopDragDropResolver
/// -- see TradeDragDropResolver's own doc comment), Plain last, always claiming. The same-entity guard lives in
/// InventoryActions/CurrencyActions themselves, so dropping back on the origin is a safe no-op. A Merged Stack moves here
/// as a whole batch (InventoryActions.TryTransferAllStacksOfItem), though it never binds to a hotbar slot.
/// </remarks>
public sealed class InventoryTransferDropHandler : IContentDropHandler
{
    private readonly MapViewState _mapViewState;
    private readonly ShopView _shopView;
    private readonly InventoryView _inventoryView;
    private readonly IPlayerQuery _playerQuery;
    private readonly TradeDragDropResolver _tradeDragDropResolver;
    private readonly IReadOnlyList<IDragDropResolver> _dragDropResolvers;

    public InventoryTransferDropHandler(MapViewState mapViewState, ShopView shopView, InventoryView inventoryView, InventoryCommands inventoryCommands, ShopCommands shopCommands, CurrencyCommands currencyCommands, IPlayerQuery playerQuery)
    {
        _mapViewState = mapViewState;
        _shopView = shopView;
        _inventoryView = inventoryView;
        _playerQuery = playerQuery;

        _tradeDragDropResolver = new TradeDragDropResolver(mapViewState, shopView, inventoryCommands, shopCommands, currencyCommands, playerQuery);
        _dragDropResolvers =
        [
            _tradeDragDropResolver,
            new ShopDragDropResolver(shopView, shopCommands),
            new PlainInventoryDragDropResolver(inventoryCommands, shopCommands, playerQuery),
        ];
    }

    /// <remarks>
    /// While a shop is open, a cell InventoryGridContent marked unable to even stage in the trade window (wrong tag, or a
    /// Merged Stack -- see InventoryItemStackCell.CanStageInTrade) never starts a drag, the same gate BuildItemContextMenu
    /// applies to "Add to trade". An unaffordable-but-tradeable item is let through: it can be staged, just not bought or
    /// sold directly. A shop's own currency can be picked up only while that shop is open, to stage in the trade-shop
    /// column -- a direct Take from a shop is still refused at the drop (see TradeDragDropResolver).
    /// </remarks>
    public bool AllowsDragStart(object payload)
    {
        if (payload is not DragPayload drag)
        {
            return true;
        }

        if (drag.Origin == DragPayloadOrigin.InventoryCell && !drag.CanStageInTrade && _mapViewState.OpenShopEntityId is not null)
        {
            return false;
        }

        if (drag.Origin == DragPayloadOrigin.Currency && drag.OriginEntityId is { } currencyOwnerEntityId)
        {
            return !_shopView.IsShop(currencyOwnerEntityId) || currencyOwnerEntityId == _mapViewState.OpenShopEntityId;
        }

        return true;
    }

    /// <summary>Refuses an item or currency held over a trade column it may never be staged in -- the other side's column, or one from the other column -- and items held over an inventory with no room for them.</summary>
    public bool IsDropRefusedAt(object payload, object? dropTarget, Point position) =>
        TryBuildContext(payload, dropTarget, out var context) && (_tradeDragDropResolver.IsRefused(context) || !HasRoomFor(context));

    /// <summary>Whether context's destination can hold the stacks it would gain -- one for an item stack, one per stack of a Merged Stack, none for currency.</summary>
    /// <remarks>The player's inventory is unlimited, a drop back on its origin moves nothing, and a shop buys and sells by its own rules, so none of them is ever full here (InventoryCapacity).</remarks>
    private bool HasRoomFor(in DragDropContext context)
    {
        var destinationEntityId = context.DestinationEntityId;
        if (destinationEntityId == _playerQuery.PlayerEntityId || destinationEntityId == context.OriginEntityId || _shopView.IsShop(destinationEntityId) || _shopView.IsShop(context.OriginEntityId))
        {
            return true;
        }

        var newStackCount = context.ItemStackInstanceId is not null ? 1 : context.MergedItemDefinitionId is { } itemDefinitionId ? CountStacksOf(context.OriginEntityId, itemDefinitionId) : 0;
        return newStackCount == 0 || InventoryCapacity.NonPlayerHasRoomForNewStacks(_inventoryView.CountStacks(destinationEntityId), newStackCount);
    }

    private readonly List<InventoryItemStackComponent> _originStacks = [];

    private int CountStacksOf(int entityId, Guid itemDefinitionId)
    {
        _originStacks.Clear();
        _inventoryView.CopyStacks(entityId, _originStacks);
        var count = 0;
        foreach (var stack in _originStacks)
        {
            if (stack.ItemDefinitionId == itemDefinitionId)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Claims a drop of an item or currency from an inventory onto any inventory (InventoryDropTarget) -- whichever grid, currency row or whole window named it.</summary>
    public bool TryDrop(object payload, object? dropTarget, Point position)
    {
        if (!TryBuildContext(payload, dropTarget, out var context))
        {
            return false;
        }

        foreach (var resolver in _dragDropResolvers)
        {
            if (resolver.TryResolve(context))
            {
                break;
            }
        }

        return true;
    }

    /// <summary>context for a drop of payload on another inventory -- false for anything else.</summary>
    private static bool TryBuildContext(object payload, object? dropTarget, out DragDropContext context)
    {
        if (payload is not DragPayload { OriginEntityId: { } originEntityId } drag || dropTarget is not InventoryDropTarget { EntityId: var destinationEntityId })
        {
            context = default;
            return false;
        }

        context = new DragDropContext(originEntityId, destinationEntityId, drag.ItemStackInstanceId, drag.MergedItemDefinitionId, drag.CurrencyType);
        return true;
    }
}
