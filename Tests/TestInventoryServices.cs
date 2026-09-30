using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Views;
using Game.World;
using Presentation.UI.Inventory;

namespace Tests;

/// <summary>Builds the item windows' InventoryServices over a test's own pools, catalog and player.</summary>
internal static class TestInventoryServices
{
    public static InventoryServices Over(ComponentManager componentManager, ItemCatalog itemCatalog, IPlayerQuery playerQuery, EventBus? eventBus = null) =>
        new(
            itemCatalog,
            new InventoryView(componentManager, itemCatalog),
            new ShopView(componentManager),
            new CurrencyView(componentManager),
            new ActionStateView(componentManager, localTierRoster: null),
            new InventoryCommands(componentManager, itemCatalog, playerQuery),
            new ShopCommands(componentManager, itemCatalog, eventBus ?? new EventBus(), playerQuery),
            new CurrencyCommands(componentManager));
}
