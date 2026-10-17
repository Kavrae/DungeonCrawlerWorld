using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Shops;
using Presentation.UI.Trade;
using UIEngine.Input;
using UIEngine.UI;

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
    private readonly IReadOnlyList<IDragDropResolver> _dragDropResolvers;

    public InventoryTransferDropHandler(MapViewState mapViewState, ShopView shopView, InventoryCommands inventoryCommands, ShopCommands shopCommands, CurrencyCommands currencyCommands, IPlayerQuery playerQuery)
    {
        _mapViewState = mapViewState;
        _shopView = shopView;

        _dragDropResolvers =
        [
            new TradeDragDropResolver(mapViewState, shopView, inventoryCommands, shopCommands, currencyCommands, playerQuery),
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

    public bool TryDrop(object payload, Element? dropTarget, Point position)
    {
        if (payload is not DragPayload { OriginEntityId: { } originEntityId } drag ||
            FindDropTargetEntityId(dropTarget, position, isCurrencyDrag: drag.CurrencyType is not null) is not { } destinationEntityId)
        {
            return false;
        }

        var context = new DragDropContext(originEntityId, destinationEntityId, drag.ItemStackInstanceId, drag.MergedItemDefinitionId, drag.CurrencyType);
        foreach (var resolver in _dragDropResolvers)
        {
            if (resolver.TryResolve(context))
            {
                break;
            }
        }

        return true;
    }

    /// <summary>
    /// Walks up from element through ParentElement looking for the nearest ancestor that can
    /// resolve a drop target -- first the narrow, specific case (a Window hosting an
    /// IInventoryDropTarget: InventoryGridContent for item stacks, CurrencyRowContent for
    /// Gold/Credits), handling the drop landing on a specific cell/currency element or on empty
    /// grid/row space alike; falling back, only once that's failed for every ancestor below it, to
    /// the broader case (an IWholeWindowDropTarget -- InventoryManagementWindow/ShopWindow/
    /// TradeWindow) for a drop landing anywhere else within that window's own bounds (its title
    /// bar, border padding, the gap between a trade column's grid and footer, etc.). This whole-window
    /// fallback exists because a player shouldn't have to land a drag exactly on a narrow currency
    /// row or a cramped grid for a drop to count. IInventoryDropTarget is checked via Window.Tag,
    /// not Window.Content -- InventoryGridContent isn't always assigned as its host window's
    /// Content (see InventoryGridContent.Initialize's own doc comment: InventoryTabContent's own
    /// hosting pattern drives it manually and never calls SetContent at all, unlike
    /// SecondaryInventoryWindow's), so Tag is the one thing both hosting patterns reliably set
    /// (confirmed via a live-testing repro against the real, TabbedContent-nested
    /// InventoryManagementWindow structure -- Content-based matching silently found nothing there,
    /// even though it worked against a simpler hand-built test harness). dropPosition is passed
    /// straight through to IWholeWindowDropTarget -- already confirmed within that window's own
    /// Rectangle by construction (the hit-test that produced element never returns something
    /// outside its own containing window's bounds), it's what TradeWindow's own implementation
    /// uses to pick which of its two columns receives the drop.
    /// </summary>
    private static int? FindDropTargetEntityId(Element? element, Point dropPosition, bool isCurrencyDrag)
    {
        for (var candidate = element; candidate is not null; candidate = candidate.ParentElement)
        {
            if (candidate is Window { Tag: IInventoryDropTarget target })
            {
                return target.EntityId;
            }

            if (candidate is IWholeWindowDropTarget wholeWindowTarget)
            {
                return isCurrencyDrag ? wholeWindowTarget.ResolveCurrencyDropEntityId(dropPosition) : wholeWindowTarget.ResolveItemDropEntityId(dropPosition);
            }
        }

        return null;
    }
}
