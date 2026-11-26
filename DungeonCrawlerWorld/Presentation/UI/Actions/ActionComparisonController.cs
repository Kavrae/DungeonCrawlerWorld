using Game.Views;
using Game.World;
using Presentation.UI.Details;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Presentation.UI.Actions;

/// <summary>Owns compare mode for actions and routes action hovers: to Action Details normally, to the comparison popup while comparing -- ItemComparisonController's rules, for the Actions window.</summary>
/// <remarks>
/// Compare (Action Details' button, or an action's "Compare" option) pins Action Details beside the Actions window -- the
/// anchor -- and starts compare mode. While it lasts, hovering a comparable action (IsComparable: the same activator type,
/// the rule the grid greys the rest out by) opens the comparison popup on it, kept just below the anchor, coloured against
/// the anchor and the anchor against it; anything else only shows its name, under the refusal cursor
/// (ComparisonInputHandler). When the popup closes the anchor goes back to plain. Stop Comparison, a right press, Escape
/// or the anchor closing ends compare mode and closes both. Starting it ends an item comparison (OnComparisonStarting).
/// </remarks>
public sealed class ActionComparisonController(
    MapViewState mapViewState,
    ActionDetailsWindowController actionDetails,
    ActionDetailsWindowController comparisonPopup,
    CursorTextContent cursorTextContent,
    ActionStateView actionStateView,
    IPlayerQuery playerQuery,
    Func<Window?> actionsWindow) : IActionDetailsPopup, IComparisonMode
{
    /// <summary>Shown at the cursor for as long as compare mode lasts.</summary>
    private const string CompareModeMessage = "Hover an action to compare";

    /// <summary>True from Arm until ClearComparison.</summary>
    public bool IsComparing { get; private set; }

    /// <summary>Settable late-bound: called as compare mode starts, before anything is shown -- ends the other compare mode.</summary>
    public Action? OnComparisonStarting { get; set; }

    public bool OpensFor(in ActionHoverTarget target) => !IsComparing || IsComparable(target.Subject);

    /// <summary>Whether compare mode can compare subject with the anchor: an action (not a menu command) whose activator is the anchor's kind (MapViewState.CompareRequiredActionActivatorType).</summary>
    public bool IsComparable(ActionCellSubject subject) =>
        subject.ActionId is { } actionId &&
        mapViewState.CompareRequiredActionActivatorType is { } requiredType &&
        actionStateView.TryGetEffectiveAction(playerQuery.PlayerEntityId, actionId, out var action) &&
        action.Activator.GetType() == requiredType;

    public bool IsRefusedAt(Element? hoveredElement) => IsComparing && hoveredElement is ActionCell { CompareState: Content.CellCompareState.Ineligible };

    public bool IsShowing(in ActionHoverTarget target) =>
        IsComparing ? actionDetails.IsShowing(target) || comparisonPopup.IsShowing(target) : actionDetails.IsShowing(target);

    public void OpenAtCursor(in ActionHoverTarget target, Window widthSourceWindow)
    {
        if (!IsComparing)
        {
            actionDetails.OpenAtCursor(target.Subject, widthSourceWindow, []);
            return;
        }

        if (actionDetails.CurrentAction is not { } anchor || !IsComparable(target.Subject) ||
            !actionStateView.TryGetEffectiveAction(playerQuery.PlayerEntityId, target.Subject.ActionId!.Value, out var hovered))
        {
            return;
        }

        comparisonPopup.OpenAtCursor(target.Subject, widthSourceWindow, [anchor]);
        actionDetails.UpdateComparedAgainst([hovered]);
    }

    /// <summary>The comparison popup closed: the anchor goes back to plain.</summary>
    public void HandleComparisonPopupClosed() => actionDetails.UpdateComparedAgainst([]);

    /// <summary>Starts compare mode on the player's action actionId: shows it in Action Details pinned beside the Actions window. A different anchor than the one shown ends any comparison first; a no-op with no Actions window open or an action the player doesn't have.</summary>
    public void Compare(Guid actionId)
    {
        if (actionsWindow() is not { } window || !actionStateView.TryGetEffectiveAction(playerQuery.PlayerEntityId, actionId, out var action))
        {
            return;
        }

        if (IsComparing && actionDetails.CurrentAction?.Id != actionId)
        {
            ClearComparison();
        }

        OnComparisonStarting?.Invoke();
        actionDetails.OpenAtCursor(ActionCellSubject.Action(actionId), window, []);
        IsComparing = true;
        mapViewState.CompareRequiredActionActivatorType = action.Activator.GetType();
        cursorTextContent.ShowPersistent(this, CompareModeMessage);
        actionDetails.PinBeside(window);
    }

    /// <summary>Ends compare mode and closes Action Details and the comparison popup -- a no-op outside compare mode.</summary>
    public void ClearComparison()
    {
        if (!IsComparing)
        {
            return;
        }

        IsComparing = false;
        mapViewState.CompareRequiredActionActivatorType = null;
        cursorTextContent.HidePersistent(this);
        actionDetails.Close();
        comparisonPopup.Close();
    }
}
