using Game.Modules.Actions;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Input;
using Presentation.UI.Details;
using Presentation.UI.Inventory;
using Presentation.UI.Menus;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.UI.Actions;

/// <summary>Owns one Action Details popup, on the details popups' shared lifecycle (DetailsPopupController): one action or menu command at a time, shown as the player would use it.</summary>
/// <remarks>
/// The game has two, as for items: Action Details itself and, while comparing, the hovered action's comparison
/// (ActionComparisonController routes hovers between them). It stays open while the pointer is over the shown cell. Its
/// width is the Actions window's; its mode is Item Details' setting, so the player's choice holds across both.
/// </remarks>
public sealed class ActionDetailsWindowController(
    ElementPoolService elementPoolService,
    Vector2 screenSize,
    PointerState pointerState,
    ElementGroup actionWindows,
    ItemDetailsModeSetting modeSetting,
    ActionStateView actionStateView,
    HotkeyBindingView hotkeyBindingView,
    MenuCommandCatalog menuCommands,
    IPlayerQuery playerQuery,
    bool closesOnPressOutside = true,
    bool closesWhenPointerLeaves = true,
    bool isComparisonPane = false)
    : DetailsPopupController<ActionDetailsWindow>(elementPoolService, pointerState, actionWindows, () => screenSize, closesOnPressOutside, closesWhenPointerLeaves)
{
    /// <summary>The Actions window the open popup takes its width from.</summary>
    private Window? _widthSourceWindow;

    /// <summary>What the open popup shows, if anything.</summary>
    public ActionCellSubject? Shown { get; private set; }

    /// <summary>The action shown, if any -- the comparison's anchor identity while pinned.</summary>
    public ActionDefinition? CurrentAction => Window?.Definition;

    /// <summary>Settable late-bound callback for the window's own "Compare" button -- none on a popup that leaves it unset.</summary>
    public Action<Guid>? OnCompareRequested { get; set; }

    /// <summary>Settable late-bound callback for the window's "Stop Comparison" button, shown while pinned.</summary>
    public Action? OnStopComparisonRequested { get; set; }

    public bool IsShowing(in ActionHoverTarget target) => Window is not null && Shown == target.Subject;

    /// <summary>Shows subject at the cursor, its width widthSourceWindow's, its lines coloured against comparedAgainst -- a no-op when the player no longer has the action or no command claims its Guid.</summary>
    public void OpenAtCursor(ActionCellSubject subject, Window widthSourceWindow, IReadOnlyList<ActionDefinition> comparedAgainst)
    {
        if (!CanShow(subject))
        {
            return;
        }

        _widthSourceWindow = widthSourceWindow;
        Shown = subject;
        Show(widthSourceWindow, window => Configure(window, subject, widthSourceWindow.ContentSize.X, comparedAgainst), Wire);
    }

    /// <summary>Re-Configures the open window with the action it already shows, its lines coloured against comparedAgainst instead -- a no-op if nothing is open.</summary>
    public void UpdateComparedAgainst(IReadOnlyList<ActionDefinition> comparedAgainst)
    {
        if (Window is { } window && Shown is { } shown && _widthSourceWindow is { } widthSourceWindow)
        {
            Configure(window, shown, widthSourceWindow.ContentSize.X, comparedAgainst);
        }
    }

    protected override bool IsPointerOverShown(Element element) => element is ActionCell cell && cell.Subject == Shown;

    protected override void OnWindowClosed()
    {
        _widthSourceWindow = null;
        Shown = null;
    }

    private bool CanShow(ActionCellSubject subject) =>
        subject.MenuCommandId is { } menuCommandId ? menuCommands.TryGet(menuCommandId, out _)
        : subject.ActionId is { } actionId && actionStateView.TryGetEffectiveAction(playerQuery.PlayerEntityId, actionId, out _);

    private void Configure(ActionDetailsWindow window, ActionCellSubject subject, float width, IReadOnlyList<ActionDefinition> comparedAgainst)
    {
        if (subject.MenuCommandId is { } menuCommandId && menuCommands.TryGet(menuCommandId, out var command))
        {
            window.ConfigureCommand(command, BoundSlotsText(menuCommandId), width);
            return;
        }

        if (subject.ActionId is { } actionId && actionStateView.TryGetEffectiveAction(playerQuery.PlayerEntityId, actionId, out var action))
        {
            window.Configure(action, width, comparedAgainst);
        }
    }

    private void Wire(ActionDetailsWindow window)
    {
        window.OnCompareRequested = OnCompareRequested is null || isComparisonPane ? null : actionId => OnCompareRequested?.Invoke(actionId);
        window.IsComparing = () => IsPinned;
        window.OnStopComparisonRequested = () => OnStopComparisonRequested?.Invoke();
        window.ChosenMode = () => modeSetting.Chosen;
        window.OnModeToggleRequested = modeSetting.Toggle;
        window.AlwaysSimple = isComparisonPane;
    }

    /// <summary>"Bound to Y, P" -- the keys of every slot holding the command -- or that no slot holds it.</summary>
    private string BoundSlotsText(Guid menuCommandId)
    {
        var keys = new List<string>();
        foreach (var entry in HotkeySlotLayout.Entries)
        {
            if (hotkeyBindingView.TryGetBoundMenuCommand(playerQuery.PlayerEntityId, entry.Slot, out var boundCommandId) && boundCommandId == menuCommandId)
            {
                keys.Add(HotkeySlotLayout.GetKeyLabel(entry.Slot));
            }
        }

        return keys.Count == 0 ? "Not bound to a slot" : $"Bound to {string.Join(", ", keys)}";
    }
}
