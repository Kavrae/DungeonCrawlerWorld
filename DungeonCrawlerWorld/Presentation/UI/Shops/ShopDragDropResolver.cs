using Game.Modules.Shops;
using Game.Views;
using Presentation.Input.DragDrop;

namespace Presentation.UI.Shops;

/// <summary>
/// The Shop feature's own drag-drop resolution -- claims a drag whenever either endpoint is a
/// shop-registered entity, so UiInputController never needs to know ShopActions or shop pricing
/// eligibility itself. Registered before PlainInventoryDragDropResolver (a shop-touching drag must
/// never fall through to a plain transfer) and after TradeDragDropResolver (a trade-offer entity is
/// never itself shop-registered, but a trade column's real shop-side counterpart is -- Trade gets
/// first refusal on anything touching its own reserved entities).
/// </summary>
internal sealed class ShopDragDropResolver(ShopView shopView, ShopCommands shopCommands) : IDragDropResolver
{
    public bool TryResolve(in DragDropContext context)
    {
        var originIsShop = shopView.IsShop(context.OriginEntityId);
        var destinationIsShop = shopView.IsShop(context.DestinationEntityId);

        if (context.ItemStackInstanceId is { } stackInstanceId)
        {
            if (originIsShop)
            {
                // Dragged out of the shop's own grid, into the player's -- a purchase.
                shopCommands.TryBuyFromShop(context.DestinationEntityId, context.OriginEntityId, stackInstanceId);
                return true;
            }

            if (destinationIsShop)
            {
                // Dragged out of the player's own grid, into the shop's -- a sale.
                shopCommands.TrySellToShop(context.OriginEntityId, context.DestinationEntityId, stackInstanceId);
                return true;
            }

            return false;
        }

        if (context.MergedItemDefinitionId is not null)
        {
            // A shop's own stock never diverges (ShopStock.GrantRandomStock only ever calls
            // InventoryActions.AddItem, which merges same-item stacks), so a Merged Stack drag
            // touching a shop shouldn't normally arise -- claim and refuse outright rather than
            // falling through to an unpriced batch transfer, keeping that guarantee even if it did.
            return originIsShop || destinationIsShop;
        }

        if (context.CurrencyType is not null)
        {
            // A shop's own currency can never be directly dragged out -- claim and no-op. A
            // non-shop-origin currency drag (including one landing on a shop, an ordinary Give) is
            // left to PlainInventoryDragDropResolver, which already routes through the same
            // shop-aware ShopActions.TryGiveCurrencyToShop chokepoint.
            return originIsShop;
        }

        return false;
    }
}
