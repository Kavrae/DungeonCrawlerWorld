using Engine.ECS.Systems;
using Engine.Tags;
using Game.Modules.Actions;
using Game.Views;
using Presentation.UI.Content;
using Presentation.UI.Menus;
using UIEngine.UI;

namespace Presentation.UI.Actions;

/// <summary>What the Actions window and its grids read and do: the player's actions and their state, the menu commands, the action categories and the frame, and the arming, targeting mode, context menus and Action Details hovering a cell's gestures use, and the view its grids show.</summary>
public sealed record ActionsWindowServices(
    ActionStateView ActionStateView,
    HotkeyBindingView HotkeyBindingView,
    EntityActions EntityActions,
    MenuCommandCatalog MenuCommands,
    GameplayTagRegistry GameplayTags,
    SimulationClock SimulationClock,
    ActionTargetingController ActionTargeting,
    MapViewState MapViewState,
    ContextMenuController ContextMenus,
    ActionHoverDetails ActionHoverDetails,
    GridViewSetting GridView);
