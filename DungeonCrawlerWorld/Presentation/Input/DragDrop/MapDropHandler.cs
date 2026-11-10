using Game.Modules.ItemPiles;
using Game.Modules.Shops;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Inventory;
using UIEngine.Input;

namespace Presentation.Input.DragDrop;

/// <summary>An item or currency dropped on the map goes into the container or corpse on that tile, or else is put down on the ground there.</summary>
/// <remarks>
/// What the player drags out of their own inventory or currency row (the whole stack, every stack of a Merged Stack, or all of
/// one currency) is put down by IPlayerItemDrops.TryDropAt, the rule a menu Drop follows too: into a corpse or a container on
/// any of its footprint tiles, or onto the ground on the player's own tile or one beside it. Anything else -- dragged from a
/// corpse's or a container's window -- goes into a container on the tile (IMapViewQuery.FindDropReceiver) through the same transfer a drop
/// on its window makes (InventoryTransferDropHandler), and onto the ground never. While held over a container, it is lit green
/// when it would take the drop and red when it wouldn't (MapViewState.ContainerDropHighlight): no room, out of reach, its loot
/// reserved for someone else, or what is held came from a shop or a trade column. A standing shop always refuses: its items
/// move only through its window. A refused drop leaves the item where it was. One dragged from a hotbar slot is only unbound
/// there (HotbarBindingDropHandler).
/// </remarks>
public sealed class MapDropHandler(IMapViewQuery mapView, ShopView shopView, MapViewState mapViewState, InventoryTransferDropHandler inventoryTransferDropHandler, IPlayerItemDrops playerItemDrops) : IContentDropHandler
{
    public void OnDragOver(object payload, object? dropTarget, Point position) =>
        mapViewState.ContainerDropHighlight = TryFindContainer(payload, dropTarget, out var containerEntityId, out var accepts, position)
            ? (containerEntityId, accepts)
            : null;

    public void OnDragEnded(object payload) => mapViewState.ContainerDropHighlight = null;

    public bool IsDropRefusedAt(object payload, object? dropTarget, Point position)
    {
        if (TryFindContainer(payload, dropTarget, out _, out var accepts, position))
        {
            return !accepts;
        }

        if (!IsGroundDropAttempt(payload, dropTarget, out var hit))
        {
            return false;
        }

        return !TryGetGroundDrop(payload, out var drop) || !playerItemDrops.CanDropAt(drop, hit.Tile);
    }

    public bool TryDrop(object payload, object? dropTarget, Point position)
    {
        if (TryFindContainer(payload, dropTarget, out var containerEntityId, out var accepts, position))
        {
            if (!accepts)
            {
                return true;
            }

            if (TryGetGroundDrop(payload, out var playerDrop) && dropTarget is MapPointerHit containerHit)
            {
                playerItemDrops.TryDropAt(playerDrop, containerHit.Tile);
            }
            else
            {
                inventoryTransferDropHandler.TryDrop(payload, new InventoryDropTarget(containerEntityId), position);
            }

            return true;
        }

        if (!IsGroundDropAttempt(payload, dropTarget, out var hit))
        {
            return false;
        }

        if (TryGetGroundDrop(payload, out var drop))
        {
            playerItemDrops.TryDropAt(drop, hit.Tile);
        }

        return true;
    }

    /// <summary>A drag from an inventory or a currency row, not a hotbar slot, released over a map tile with no container that takes it -- one this handler answers for, putting it down or refusing it.</summary>
    private static bool IsGroundDropAttempt(object payload, object? dropTarget, out MapPointerHit hit)
    {
        hit = null!;
        if (dropTarget is not MapPointerHit mapHit || payload is not DragPayload { OriginEntityId: not null, OriginSlot: null })
        {
            return false;
        }

        hit = mapHit;
        return true;
    }

    /// <summary>What a drag of payload puts down on the ground: the player's own whole stack, Merged Stack or currency. False for anything from another inventory.</summary>
    private bool TryGetGroundDrop(object payload, out ItemDrop drop)
    {
        drop = default;
        return payload is DragPayload drag && drag.TryGetGroundDrop(mapView.PlayerEntityId, out drop);
    }

    /// <summary>The container or corpse on the tile dropTarget names that a drop of payload from an inventory would go into, and whether it would take it -- false off the map, for a drag from no inventory, or a tile with none.</summary>
    /// <remarks>A standing shop on the tile is found and never takes the drop: items reach a shop only through its window, and a drop there must not land on the ground under it instead.</remarks>
    private bool TryFindContainer(object payload, object? dropTarget, out int containerEntityId, out bool accepts, Point position)
    {
        containerEntityId = -1;
        accepts = false;
        if (dropTarget is not MapPointerHit hit || payload is not DragPayload { OriginEntityId: { } originEntityId })
        {
            return false;
        }

        var receiver = mapView.FindDropReceiver(hit.OccupantEntityIds, out containerEntityId);
        if (receiver == DropReceiverKind.Ground)
        {
            return false;
        }

        if (receiver == DropReceiverKind.Shop)
        {
            return true;
        }

        if (TryGetGroundDrop(payload, out var playerDrop))
        {
            accepts = playerItemDrops.CanDropAt(playerDrop, hit.Tile);
            return true;
        }

        accepts =
            !shopView.IsShop(originEntityId) && !IsTradeOfferEntity(originEntityId) &&
            MapWindow.AllowsAccess(mapView.GetInteraction(containerEntityId).AccessRefusal) &&
            !inventoryTransferDropHandler.IsDropRefusedAt(payload, new InventoryDropTarget(containerEntityId), position);
        return true;
    }

    private bool IsTradeOfferEntity(int entityId) =>
        mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == entityId || mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == entityId;
}
