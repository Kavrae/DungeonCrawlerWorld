using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI.Inventory;
using Presentation.UI.Menus;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Presentation.UI.Actions;

/// <summary>Builds Action Details, its comparison popup and action compare mode, and owns the rules between them -- ItemWindowCoordinator's Item Details half, for actions.</summary>
/// <remarks>
/// The hover details are built first (the Actions window's grids take them), and the popups once the layers exist
/// (Initialize). Both popups and the Actions window are actionWindows, so a press outside all of them closes Action
/// Details. Action Details closing ends compare mode; the comparison popup sits just below the pinned anchor.
/// </remarks>
public sealed class ActionDetailsCoordinator
{
    private readonly ActionDetailsWindowController _comparisonPopup;

    public ActionDetailsCoordinator(
        ElementPoolService elementPoolService,
        Vector2 screenSize,
        PointerState pointerState,
        CursorTextContent cursorTextContent,
        ElementGroup actionWindows,
        ItemDetailsModeSetting modeSetting,
        MapViewState mapViewState,
        ActionStateView actionStateView,
        HotkeyBindingView hotkeyBindingView,
        MenuCommandCatalog menuCommands,
        IPlayerQuery playerQuery,
        Func<Window?> actionsWindow)
    {
        HoverDetails = new ActionHoverDetails(cursorTextContent);
        ActionDetails = new ActionDetailsWindowController(elementPoolService, screenSize, pointerState, actionWindows, modeSetting, actionStateView, hotkeyBindingView, menuCommands, playerQuery);
        _comparisonPopup = new ActionDetailsWindowController(elementPoolService, screenSize, pointerState, actionWindows, modeSetting, actionStateView, hotkeyBindingView, menuCommands, playerQuery, closesOnPressOutside: false, closesWhenPointerLeaves: false, isComparisonPane: true);
        Comparison = new ActionComparisonController(mapViewState, ActionDetails, _comparisonPopup, cursorTextContent, actionStateView, playerQuery, actionsWindow);

        HoverDetails.Popup = Comparison;
        ActionDetails.OnClosed = Comparison.ClearComparison;
        ActionDetails.OnStopComparisonRequested = Comparison.ClearComparison;
        ActionDetails.OnCompareRequested = Comparison.Compare;
        _comparisonPopup.OnClosed = Comparison.HandleComparisonPopupClosed;
        _comparisonPopup.FixedPosition = () => new Vector2(ActionDetails.Rectangle.Left, ActionDetails.Rectangle.Bottom + WindowCascadePlacement.Gap);
    }

    public ActionHoverDetails HoverDetails { get; }

    public ActionDetailsWindowController ActionDetails { get; }

    public ActionComparisonController Comparison { get; }

    public void Initialize(UiLayerStack layers)
    {
        ActionDetails.Initialize(layers);
        _comparisonPopup.Initialize(layers);
    }

    /// <summary>An action drag has started: ends compare mode and closes Action Details and the comparison popup.</summary>
    public void CloseForDrag()
    {
        Comparison.ClearComparison();
        _comparisonPopup.Close();
        ActionDetails.Close();
    }
}
