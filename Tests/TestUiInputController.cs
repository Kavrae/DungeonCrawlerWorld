using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.Shops;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.Input.DragDrop;
using Presentation.UI;
using Presentation.UI.AbilityScores;
using Presentation.UI.Content;
using Presentation.UI.Diagnostics;
using Presentation.UI.Inventory;
using UIEngine.Input;
using UIEngine.UI;

namespace Tests;

/// <summary>Builds a UiInputController from the pieces a test already holds, making its command services over them.</summary>
/// <remarks>Registers the game's input and drop handlers in the game's order (ShellBootstrapper), each only when the test supplies what it needs -- the hotbar drop handler when the layers already hold a hotbar.</remarks>
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
        ItemComparisonController? itemComparisonController = null,
        MapViewState? mapViewState = null,
        HealthWindowController? healthWindowController = null,
        InventoryWindowController? inventoryWindowController = null,
        AbilityScoreWindowController? abilityScoreWindowController = null,
        DiagnosticsWindowController? diagnosticsWindowController = null,
        PointerState? pointerState = null,
        TargetingModeSwitch? targetingModeSwitch = null)
    {
        List<IUiInputHandler> inputHandlers = [new AdminModeInputHandler()];
        AddKeyPress(inputHandlers, Keys.U, healthWindowController is null ? null : healthWindowController.ToggleHealthWindow);
        AddKeyPress(inputHandlers, Keys.I, inventoryWindowController is null ? null : inventoryWindowController.ToggleInventoryWindow);
        AddKeyPress(inputHandlers, Keys.O, abilityScoreWindowController is null ? null : abilityScoreWindowController.ToggleAbilityScoreWindow);
        AddKeyPress(inputHandlers, Keys.F3, diagnosticsWindowController is null ? null : diagnosticsWindowController.ToggleDiagnosticsWindow);
        AddKeyPress(inputHandlers, Keys.LeftAlt, targetingModeSwitch is null ? null : targetingModeSwitch.Switch);

        if (itemComparisonController is not null)
        {
            inputHandlers.Add(new ItemComparisonDisarmInputHandler(itemComparisonController));
        }

        if (hotbarController is not null)
        {
            inputHandlers.Add(new HotbarInputHandler(hotbarController));
        }

        inputHandlers.Add(new ActivationRefusalInputHandler());

        List<IContentDropHandler> contentDropHandlers =
        [
            new InventoryTransferDropHandler(mapViewState ?? new MapViewState(), new ShopView(componentManager), new InventoryView(componentManager, itemCatalog), new InventoryCommands(componentManager, itemCatalog, playerQuery), new ShopCommands(componentManager, itemCatalog, eventBus, playerQuery), new CurrencyCommands(componentManager), playerQuery),
        ];

        if (layers[UiLayer.StaticHud].OfType<Window>().Select(window => window.Content).OfType<HotbarContent>().FirstOrDefault() is { } hotbarContent)
        {
            contentDropHandlers.Insert(0, new HotbarBindingDropHandler(hotbarContent, playerQuery));
        }

        return new(layers, screenSize, pointerState ?? new PointerState(), inputHandlers, contentDropHandlers, contextMenuController?.Dropdowns);
    }

    private static void AddKeyPress(List<IUiInputHandler> inputHandlers, Keys key, Action? onPressed)
    {
        if (onPressed is not null)
        {
            inputHandlers.Add(new KeyPressInputHandler(key, onPressed));
        }
    }
}
