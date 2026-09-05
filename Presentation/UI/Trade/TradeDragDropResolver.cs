using System;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.World;
using Presentation.Input.DragDrop;
using Presentation.UI;

namespace Presentation.UI.Trade;

/// <summary>
/// The Trade window's own drag-drop resolution -- claims a drag whenever either endpoint is one of
/// the two reserved trade-offer entities (MapViewState.ReservedEntityIds.TradeOfferPlayerEntityId/
/// TradeOfferShopEntityId), so UiInputController never needs to know about trade-offer entities or
/// PLAN-trade-window.md's own eligibility table itself. Registered before ShopDragDropResolver: a
/// trade-offer entity is never itself shop-registered (ShopComponent lives on the real shop only),
/// but ShopActions.TryBuyFromShop/TrySellToShop debit/credit whichever entity id they're given
/// directly, so letting ShopDragDropResolver see a trade-offer entity id at all would try to move
/// Gold through its own, always-empty CurrencyComponent instead of the real player's or shop's --
/// this resolver must get first refusal on anything touching its own reserved entities.
/// </summary>
internal sealed class TradeDragDropResolver : IDragDropResolver
{
    private readonly MapViewState _mapViewState;
    private readonly PackedComponentPool<ShopComponent>? _shopPool;
    private readonly ItemCatalog? _itemCatalog;
    private readonly IPlayerQuery? _playerQuery;

    public TradeDragDropResolver(MapViewState mapViewState, PackedComponentPool<ShopComponent>? shopPool, ItemCatalog? itemCatalog, IPlayerQuery? playerQuery)
    {
        _mapViewState = mapViewState;
        _shopPool = shopPool;
        _itemCatalog = itemCatalog;
        _playerQuery = playerQuery;
    }

    /// <summary>True for either of the two reserved trade-offer entities -- false (never a false positive) whenever no trade window was ever wired, or entityId is any ordinary entity.</summary>
    private bool IsTradeOfferEntity(int entityId) =>
        _mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == entityId || _mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == entityId;

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
            ResolveItemDrag(context.ComponentManager, context.OriginEntityId, context.DestinationEntityId, stackInstanceId);
            return true;
        }

        if (context.MergedItemDefinitionId is not null)
        {
            // PLAN-trade-window.md's eligibility rules are only worked out for single,
            // StackInstanceId-tracked stacks -- claim and refuse outright.
            return true;
        }

        if (context.CurrencyType is { } currencyType)
        {
            ResolveCurrencyDrag(context.ComponentManager, context.OriginEntityId, context.DestinationEntityId, currencyType);
            return true;
        }

        return true;
    }

    /// <summary>
    /// Implements PLAN-trade-window.md's own "Drag-drop eligibility" table -- each branch below is
    /// one named row/column of it, in the same order.
    /// </summary>
    private void ResolveItemDrag(ComponentManager componentManager, int originEntityId, int destinationEntityId, Guid stackInstanceId)
    {
        var originIsShop = _shopPool?.Has(originEntityId) == true;
        var destinationIsShop = _shopPool?.Has(destinationEntityId) == true;
        var isOriginTradePlayer = _mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == originEntityId;
        var isOriginTradeShop = _mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == originEntityId;
        var isDestinationTradePlayer = _mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == destinationEntityId;
        var isDestinationTradeShop = _mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == destinationEntityId;

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
            InventoryActions.TryTransferStack(componentManager, originEntityId, destinationEntityId, stackInstanceId, _playerQuery);
            return;
        }

        // Trade: player column -> Shop grid: direct sell. Composed from a remove-from-trade
        // transfer (the stack must land back in the real player's own inventory first --
        // ShopActions.TrySellToShop can't take the trade entity id directly) followed by the
        // ordinary sell action, preserving stackInstanceId throughout; undone (moved back into the
        // trade column) if the sale itself fails.
        if (isOriginTradePlayer && destinationIsShop)
        {
            if (_itemCatalog is { } sellCatalog && _playerQuery is { } sellerQuery)
            {
                var realPlayerEntityId = sellerQuery.PlayerEntityId;
                if (InventoryActions.TryTransferStack(componentManager, originEntityId, realPlayerEntityId, stackInstanceId, _playerQuery) &&
                    !ShopActions.TrySellToShop(componentManager, sellCatalog, realPlayerEntityId, destinationEntityId, stackInstanceId, _playerQuery))
                {
                    InventoryActions.TryTransferStack(componentManager, realPlayerEntityId, originEntityId, stackInstanceId, _playerQuery);
                }
            }

            return;
        }

        // Trade: shop column -> Player Inventory (or any other non-shop, non-trade destination):
        // direct buy, the mirror of direct sell above -- return the stack to the real shop first
        // (via MapViewState.OpenShopEntityId, since the trade-shop-column origin has no other
        // identity to resolve the real shop from), then the ordinary buy action pays with (and
        // delivers into) destinationEntityId exactly as an ordinary Shop grid -> Player Inventory
        // drag already does; undone if the purchase itself fails.
        if (isOriginTradeShop && _mapViewState.OpenShopEntityId is { } realShopEntityId && _itemCatalog is { } buyCatalog)
        {
            if (InventoryActions.TryTransferStack(componentManager, originEntityId, realShopEntityId, stackInstanceId, _playerQuery) &&
                !ShopActions.TryBuyFromShop(componentManager, buyCatalog, destinationEntityId, realShopEntityId, stackInstanceId, _playerQuery))
            {
                InventoryActions.TryTransferStack(componentManager, realShopEntityId, originEntityId, stackInstanceId, _playerQuery);
            }

            return;
        }

        // Anything else still touching the shop column at this point (Player Inventory -> Trade:
        // shop column, Shop grid -> Trade: player column, and the reverse of each -- plus the
        // degenerate case just above where no shop was actually open, or no ItemCatalog was wired)
        // is not allowed -- only the real shop's own stock may ever populate the shop column, and
        // only a plain inventory may ever populate the player column.
        if (isDestinationTradeShop || isOriginTradeShop)
        {
            return;
        }

        // Whatever's left is a plain stage/unstage between a trade column and an ordinary, non-shop
        // inventory (Player Inventory <-> Trade: player column) -- no transaction, just a stack
        // moving between two entities' own InventoryItemStackComponent pools.
        InventoryActions.TryTransferStack(componentManager, originEntityId, destinationEntityId, stackInstanceId, _playerQuery);
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
    private void ResolveCurrencyDrag(ComponentManager componentManager, int originEntityId, int destinationEntityId, CurrencyType currencyType)
    {
        var isOriginTradePlayer = _mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == originEntityId;
        var isOriginTradeShop = _mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == originEntityId;
        var isDestinationTradePlayer = _mapViewState.ReservedEntityIds?.TradeOfferPlayerEntityId == destinationEntityId;
        var isDestinationTradeShop = _mapViewState.ReservedEntityIds?.TradeOfferShopEntityId == destinationEntityId;

        var realPlayerEntityId = _playerQuery?.PlayerEntityId;
        var isPlayerStageUnstage = (originEntityId == realPlayerEntityId && isDestinationTradePlayer) ||
            (isOriginTradePlayer && destinationEntityId == realPlayerEntityId);

        var realShopEntityId = _mapViewState.OpenShopEntityId;
        var isShopStageUnstage = (originEntityId == realShopEntityId && isDestinationTradeShop) ||
            (isOriginTradeShop && destinationEntityId == realShopEntityId);

        if (isPlayerStageUnstage || isShopStageUnstage)
        {
            CurrencyActions.TryTransfer(componentManager, originEntityId, destinationEntityId, currencyType);
        }
    }
}
