using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.World;

namespace Game.Modules.Shops;

/// <summary>A session's shop transactions, for code outside a system: ShopActions with the session's pools, catalog, bus and player bound.</summary>
public sealed class ShopCommands(ComponentManager componentManager, ItemCatalog itemCatalog, EventBus eventBus, IPlayerQuery playerQuery)
{
    /// <inheritdoc cref="ShopActions.TryBuyFromShop"/>
    public bool TryBuyFromShop(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        ShopActions.TryBuyFromShop(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery);

    /// <inheritdoc cref="ShopActions.TrySellToShop"/>
    public bool TrySellToShop(int playerEntityId, int shopEntityId, uint stackInstanceId) =>
        ShopActions.TrySellToShop(componentManager, itemCatalog, playerEntityId, shopEntityId, stackInstanceId, playerQuery);

    /// <inheritdoc cref="ShopActions.TryGiveCurrencyToShop"/>
    public bool TryGiveCurrencyToShop(int sourceEntityId, int destinationEntityId, CurrencyType currencyType, int? eventPlayerEntityId = null) =>
        ShopActions.TryGiveCurrencyToShop(componentManager, eventBus, sourceEntityId, destinationEntityId, currencyType, eventPlayerEntityId);
}
