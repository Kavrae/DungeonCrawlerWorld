using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Input;
using Presentation.UI;
using Presentation.UI.AbilityScores;
using Presentation.UI.Diagnostics;
using Presentation.UI.Inventory;

namespace Tests;

/// <summary>Builds a UiInputController from the pieces a test already holds, making its command services over them.</summary>
internal static class TestUiInputController
{
    public static UiInputController Create(
        UiLayerStack layers,
        Vector2 screenSize,
        ComponentManager componentManager,
        IPlayerQuery playerQuery,
        EventBus eventBus,
        ItemCatalog itemCatalog,
        HotbarController? hotbarController = null,
        ContextMenuController? contextMenuController = null,
        ItemDetailsWindowController? itemDetailsWindowController = null,
        ItemComparisonController? itemComparisonController = null,
        MapViewState? mapViewState = null,
        HealthWindowController? healthWindowController = null,
        InventoryWindowController? inventoryWindowController = null,
        AbilityScoreWindowController? abilityScoreWindowController = null,
        DiagnosticsWindowController? diagnosticsWindowController = null,
        PointerState? pointerState = null,
        TargetingModeSwitch? targetingModeSwitch = null) =>
        new(
            layers,
            screenSize,
            pointerState ?? new PointerState(),
            new ShopView(componentManager),
            playerQuery,
            new InventoryCommands(componentManager, itemCatalog, playerQuery),
            new ShopCommands(componentManager, itemCatalog, eventBus, playerQuery),
            new CurrencyCommands(componentManager),
            hotbarController,
            contextMenuController,
            itemDetailsWindowController,
            itemComparisonController,
            mapViewState,
            healthWindowController,
            inventoryWindowController,
            abilityScoreWindowController,
            diagnosticsWindowController,
            targetingModeSwitch);
}
