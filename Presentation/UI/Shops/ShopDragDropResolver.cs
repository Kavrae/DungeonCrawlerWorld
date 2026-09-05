using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.World;
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
internal sealed class ShopDragDropResolver : IDragDropResolver
{
    private readonly PackedComponentPool<ShopComponent> _shopPool;
    private readonly ItemCatalog? _itemCatalog;
    private readonly IPlayerQuery? _playerQuery;

    public ShopDragDropResolver(PackedComponentPool<ShopComponent> shopPool, ItemCatalog? itemCatalog, IPlayerQuery? playerQuery)
    {
        _shopPool = shopPool;
        _itemCatalog = itemCatalog;
        _playerQuery = playerQuery;
    }

    public bool TryResolve(in DragDropContext context)
    {
        var originIsShop = _shopPool.Has(context.OriginEntityId);
        var destinationIsShop = _shopPool.Has(context.DestinationEntityId);

        if (context.ItemStackInstanceId is { } stackInstanceId)
        {
            // If a shop is involved but ItemCatalog was never wired, neither branch below matches --
            // this resolver returns false and the drag falls through to a plain transfer via
            // PlainInventoryDragDropResolver, same as today's degrade path.
            if (originIsShop && _itemCatalog is { } buyCatalog)
            {
                // Dragged out of the shop's own grid, into the player's -- a purchase.
                ShopActions.TryBuyFromShop(context.ComponentManager, buyCatalog, context.DestinationEntityId, context.OriginEntityId, stackInstanceId, _playerQuery);
                return true;
            }

            if (destinationIsShop && _itemCatalog is { } sellCatalog)
            {
                // Dragged out of the player's own grid, into the shop's -- a sale.
                ShopActions.TrySellToShop(context.ComponentManager, sellCatalog, context.OriginEntityId, context.DestinationEntityId, stackInstanceId, _playerQuery);
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
