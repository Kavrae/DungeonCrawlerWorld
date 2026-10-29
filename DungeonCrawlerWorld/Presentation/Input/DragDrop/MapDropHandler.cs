using Game.Modules.Shops;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI;
using UIEngine.Input;

namespace Presentation.Input.DragDrop;

/// <summary>An item or currency dropped on the map goes into the container or corpse on that tile, as if dropped on its window.</summary>
/// <remarks>
/// The first occupant of the tile (topmost first, MapPointerHit) that can be looted -- a container that isn't a standing
/// shop, or anything dead -- takes the drop, through the same transfer a drop on its inventory window makes
/// (InventoryTransferDropHandler), so the whole stack moves. While held over it, the container is lit green when it would
/// take the drop and red when it wouldn't (MapViewState.ContainerDropHighlight): it has no room (InventoryCapacity), the
/// player isn't standing on or next to it (the Loot rule), its loot is reserved for someone else, or what is held came
/// from a shop or a trade column (bought and sold only through their windows). A red container refuses the drop and the
/// item stays where it was. A tile with no container refuses an item from an inventory; one dragged from a hotbar slot is
/// only unbound there (HotbarBindingDropHandler). Dropping on the ground plugs in here once items can lie there.
/// </remarks>
public sealed class MapDropHandler(IMapViewQuery mapView, ShopView shopView, MapViewState mapViewState, InventoryTransferDropHandler inventoryTransferDropHandler) : IContentDropHandler
{
    public void OnDragOver(object payload, object? dropTarget, Point position) =>
        mapViewState.ContainerDropHighlight = TryFindContainer(payload, dropTarget, out var containerEntityId, out var accepts, position)
            ? (containerEntityId, accepts)
            : null;

    public void OnDragEnded(object payload) => mapViewState.ContainerDropHighlight = null;

    public bool IsDropRefusedAt(object payload, object? dropTarget, Point position) =>
        TryFindContainer(payload, dropTarget, out _, out var accepts, position)
            ? !accepts
            : dropTarget is MapPointerHit && payload is DragPayload { OriginEntityId: not null, OriginSlot: null };

    public bool TryDrop(object payload, object? dropTarget, Point position)
    {
        if (!TryFindContainer(payload, dropTarget, out var containerEntityId, out var accepts, position))
        {
            return dropTarget is MapPointerHit && payload is DragPayload { OriginEntityId: not null, OriginSlot: null };
        }

        if (accepts)
        {
            inventoryTransferDropHandler.TryDrop(payload, new InventoryDropTarget(containerEntityId), position);
        }

        return true;
    }

    /// <summary>The container or corpse on the tile dropTarget names that a drop of payload from an inventory would go into, and whether it would take it -- false off the map, for a drag from no inventory, or a tile with none.</summary>
    private bool TryFindContainer(object payload, object? dropTarget, out int containerEntityId, out bool accepts, Point position)
    {
        containerEntityId = -1;
        accepts = false;
        if (dropTarget is not MapPointerHit hit || payload is not DragPayload { OriginEntityId: { } originEntityId })
        {
            return false;
        }

        foreach (var occupantEntityId in hit.OccupantEntityIds)
        {
            var interaction = mapView.GetInteraction(occupantEntityId);
            if (!interaction.IsDestroyed && (!interaction.IsContainer || interaction.IsShop))
            {
                continue;
            }

            containerEntityId = occupantEntityId;
            accepts =
                !shopView.IsShop(originEntityId) && !IsTradeOfferEntity(originEntityId) &&
                (!interaction.IsLootReservedFromPlayer || AdminMode.IsOn) &&
                MapWindow.IsAdjacentToPlayer(mapView, occupantEntityId) &&
                !inventoryTransferDropHandler.IsDropRefusedAt(payload, new InventoryDropTarget(occupantEntityId), position);
            return true;
        }

        return false;
    }

    private bool IsTradeOfferEntity(int entityId) =>
        mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == entityId || mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == entityId;
}
