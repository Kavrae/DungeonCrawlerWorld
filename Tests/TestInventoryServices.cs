using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Death;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Views;
using Game.World;
using Presentation.UI.Inventory;
using Tests.UIEngine;
using UIEngine.Input;
using UIEngine.UI.Content;

namespace Tests;

/// <summary>Builds the item windows' InventoryServices over a test's own pools, catalog and player.</summary>
internal static class TestInventoryServices
{
    /// <param name="itemHoverDetails">The hover hub the grids report to; a fresh one, with no popup, when null.</param>
    /// <param name="playerItemDrops">What Drop and Drop All arm; a TestPlayerItemDrops when null.</param>
    public static InventoryServices Over(ComponentManager componentManager, ItemCatalog itemCatalog, IPlayerQuery playerQuery, EventBus? eventBus = null, ItemHoverDetails? itemHoverDetails = null, IPlayerItemDrops? playerItemDrops = null) =>
        new(
            itemCatalog,
            new InventoryView(componentManager, itemCatalog),
            new ShopView(componentManager),
            new CurrencyView(componentManager),
            TestActionStateViews.Over(componentManager, itemCatalog: itemCatalog),
            new InventoryCommands(componentManager, itemCatalog, playerQuery),
            new ShopCommands(componentManager, itemCatalog, eventBus ?? new EventBus(), playerQuery),
            new CurrencyCommands(componentManager),
            TestGameplayTags.BuiltIn,
            itemHoverDetails ?? new ItemHoverDetails(new CursorTextContent(new PointerState(), TestFonts.Shared)),
            playerItemDrops ?? new TestPlayerItemDrops(),
            new LootCommands(componentManager, itemCatalog, playerQuery));
}
