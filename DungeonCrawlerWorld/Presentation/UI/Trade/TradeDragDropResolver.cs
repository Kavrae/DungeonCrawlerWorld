using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Views;
using Game.World;
using Presentation.Input.DragDrop;

namespace Presentation.UI.Trade;

/// <summary>
/// The Trade window's own drag-drop resolution -- claims a drag whenever either endpoint is one of
/// the two reserved trade-offer entities (MapViewState.ReservedEntityIds.TradeOfferPlayerEntityId/
/// TradeOfferShopEntityId), so UiInputController never needs to know about trade-offer entities or
/// the trade window's own eligibility rules itself. Registered before ShopDragDropResolver: a
/// trade-offer entity is never itself shop-registered (ShopComponent lives on the real shop only),
/// but ShopActions.TryBuyFromShop/TrySellToShop debit/credit whichever entity id they're given
/// directly, so letting ShopDragDropResolver see a trade-offer entity id at all would try to move
/// Gold through its own, always-empty CurrencyComponent instead of the real player's or shop's --
/// this resolver must get first refusal on anything touching its own reserved entities.
/// </summary>
internal sealed class TradeDragDropResolver(MapViewState mapViewState, ShopView shopView, InventoryCommands inventoryCommands, ShopCommands shopCommands, CurrencyCommands currencyCommands, IPlayerQuery playerQuery) : IDragDropResolver
{

    /// <summary>True for either of the two reserved trade-offer entities -- false (never a false positive) whenever no trade window was ever wired, or entityId is any ordinary entity.</summary>
    private bool IsTradeOfferEntity(int entityId) =>
        mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == entityId || mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == entityId;

    public bool TryResolve(in DragDropContext context)
    {
        var originIsTrade = IsTradeOfferEntity(context.OriginEntityId);
        var destinationIsTrade = IsTradeOfferEntity(context.DestinationEntityId);
        if (!originIsTrade && !destinationIsTrade)
        {
            return false;
        }

        if (context.ItemStackInstanceId is { } stackInstanceId)
        {
            ResolveItemDrag(context.OriginEntityId, context.DestinationEntityId, stackInstanceId);
            return true;
        }

        if (context.MergedItemDefinitionId is not null)
        {
            // The trade window's eligibility rules are only worked out for single,
            // StackInstanceId-tracked stacks -- claim and refuse outright.
            return true;
        }

        if (context.CurrencyType is { } currencyType)
        {
            ResolveCurrencyDrag(context.OriginEntityId, context.DestinationEntityId, currencyType);
            return true;
        }

        return true;
    }

    /// <summary>
    /// Implements the trade window's drag-drop eligibility rules, one branch per origin/destination
    /// pairing.
    /// </summary>
    private void ResolveItemDrag(int originEntityId, int destinationEntityId, uint stackInstanceId)
    {
        var originIsShop = shopView.IsShop(originEntityId);
        var destinationIsShop = shopView.IsShop(destinationEntityId);
        var isOriginTradePlayer = mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == originEntityId;
        var isOriginTradeShop = mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == originEntityId;
        var isDestinationTradePlayer = mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == destinationEntityId;
        var isDestinationTradeShop = mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == destinationEntityId;

        // Trade: player column <-> Trade: shop column, either direction -- never allowed; the two
        // columns only ever gain/lose stacks via the real player/shop grids, never each other.
        if ((isOriginTradePlayer && isDestinationTradeShop) || (isOriginTradeShop && isDestinationTradePlayer))
        {
            return;
        }

        // Shop grid -> Trade: player column: not allowed -- only the real shop's own stock may
        // populate the shop column (Shop grid -> Trade: shop column, just below), never the
        // player's.
        if (originIsShop && isDestinationTradePlayer)
        {
            return;
        }

        // Shop grid <-> Trade: shop column, either direction -- a free offer/restock shuffle, not a
        // real transaction; force a plain transfer instead of TryBuyFromShop/TrySellToShop below.
        if ((originIsShop && isDestinationTradeShop) || (isOriginTradeShop && destinationIsShop))
        {
            inventoryCommands.TryTransferStack(originEntityId, destinationEntityId, stackInstanceId);
            return;
        }

        // Trade: player column -> Shop grid: direct sell. Composed from a remove-from-trade
        // transfer (the stack must land back in the real player's own inventory first --
        // ShopActions.TrySellToShop can't take the trade entity id directly) followed by the
        // ordinary sell action, preserving stackInstanceId throughout; undone (moved back into the
        // trade column) if the sale itself fails.
        if (isOriginTradePlayer && destinationIsShop)
        {
            var realPlayerEntityId = playerQuery.PlayerEntityId;
            if (inventoryCommands.TryTransferStack(originEntityId, realPlayerEntityId, stackInstanceId) &&
                !shopCommands.TrySellToShop(realPlayerEntityId, destinationEntityId, stackInstanceId))
            {
                inventoryCommands.TryTransferStack(realPlayerEntityId, originEntityId, stackInstanceId);
            }

            return;
        }

        // Trade: shop column -> Player Inventory (or any other non-shop, non-trade destination):
        // direct buy, the mirror of direct sell above -- return the stack to the real shop first
        // (via MapViewState.OpenShopEntityId, since the trade-shop-column origin has no other
        // identity to resolve the real shop from), then the ordinary buy action pays with (and
        // delivers into) destinationEntityId exactly as an ordinary Shop grid -> Player Inventory
        // drag already does; undone if the purchase itself fails.
        // With no shop open there is no real shop to buy from, so the drag is refused.
        if (isOriginTradeShop)
        {
            if (mapViewState.OpenShopEntityId is { } realShopEntityId &&
                inventoryCommands.TryTransferStack(originEntityId, realShopEntityId, stackInstanceId) &&
                !shopCommands.TryBuyFromShop(destinationEntityId, realShopEntityId, stackInstanceId))
            {
                inventoryCommands.TryTransferStack(realShopEntityId, originEntityId, stackInstanceId);
            }

            return;
        }

        // Anything else still touching the shop column at this point (Player Inventory -> Trade:
        // shop column) is not allowed -- only the real shop's own stock may ever populate the shop
        // column.
        if (isDestinationTradeShop)
        {
            return;
        }

        // Whatever's left is a plain stage/unstage between a trade column and an ordinary, non-shop
        // inventory (Player Inventory <-> Trade: player column) -- no transaction, just a stack
        // moving between two entities' own InventoryItemStackComponent pools.
        inventoryCommands.TryTransferStack(originEntityId, destinationEntityId, stackInstanceId);
    }

    /// <summary>
    /// Unlike an item stack, currency has no buy/sell price against itself, so there is no "direct
    /// give/take" analog to ResolveItemDrag's composed direct-sell/direct-buy: a trade column's own
    /// currency only ever stages/unstages against its own real owner (Player &lt;-&gt; Trade: player
    /// column, Shop &lt;-&gt; Trade: shop column), a plain CurrencyActions.TryTransfer either
    /// direction. Crossing between the trade window's own two columns, or between a trade column and
    /// the *other* real entity, is refused -- the same "no direct drag between the trade window's own
    /// two columns" rule the item eligibility table already established, extended to currency for
    /// consistency even though no pricing forces it here.
    /// </summary>
    private void ResolveCurrencyDrag(int originEntityId, int destinationEntityId, CurrencyType currencyType)
    {
        var isOriginTradePlayer = mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == originEntityId;
        var isOriginTradeShop = mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == originEntityId;
        var isDestinationTradePlayer = mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == destinationEntityId;
        var isDestinationTradeShop = mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == destinationEntityId;

        var realPlayerEntityId = playerQuery.PlayerEntityId;
        var isPlayerStageUnstage = (originEntityId == realPlayerEntityId && isDestinationTradePlayer) ||
            (isOriginTradePlayer && destinationEntityId == realPlayerEntityId);

        var realShopEntityId = mapViewState.OpenShopEntityId;
        var isShopStageUnstage = (originEntityId == realShopEntityId && isDestinationTradeShop) ||
            (isOriginTradeShop && destinationEntityId == realShopEntityId);

        if (isPlayerStageUnstage || isShopStageUnstage)
        {
            currencyCommands.TryTransfer(originEntityId, destinationEntityId, currencyType);
        }
    }
}
