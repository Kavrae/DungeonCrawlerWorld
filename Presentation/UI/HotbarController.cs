using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.Content;

namespace Presentation.UI;

/// <summary>
/// Manages the Armed Hotkey Summary popup (shown/hidden through the shared TooltipController --
/// see UpdateSummary; no dedicated Element subclass, since deciding what to show is the only
/// thing that was ever specific to it, and this class already had everything UpdateSummary needs)
/// and handles hotbar slot interactions.
/// </summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class HotbarController(MapViewState mapViewState, HotbarContent hotbarContent, ActionTargetingController actionTargeting, TooltipController tooltipController)
{
    /// <summary>Fixed width for the Armed Hotkey Summary popup; effectively-unbounded height cap -- passed to TooltipController.Show on every call, since the shared Tooltip has no fixed size of its own anymore (see TooltipController.Show's own doc comment on why maximumSize is required, not defaulted).</summary>
    private static readonly Vector2 SummaryMaximumSize = new(HotbarContent.SummaryWidth, 10000f);

    private HotkeySlot? _pressedSlot;
    private HotkeySlot? _hoveredSlot;
    private int _hoveredSlotFrames;
    private HotkeySlot? _displayedSummarySlot;

    /// <summary>Called by UiInputController.HandleMousePress when the press lands on a hotbar slot.</summary>
    public void OnSlotPressed(HotkeySlot slot) => _pressedSlot = slot;

    /// <summary>Called by UiInputController.HandleMousePress when the press lands anywhere else. Resets the pressed slot.</summary>
    public void OnPressOutsideHotbar() => _pressedSlot = null;

    /// <summary>Called by UiInputController.HandleMouseRelease when the release lands on a hotbar slot.</summary>
    /// <remarks>Records the slot as pressed on the first tap. Calls the action targeting controller on the second tap of the same slot.</remarks>
    /// <param name="slot"></param>
    public void OnSlotTapped(HotkeySlot slot)
    {
        var wasPressedSlot = _pressedSlot == slot;
        _pressedSlot = null;

        if (!wasPressedSlot)
        {
            return;
        }

        actionTargeting.HandleHotkeySlotPress(slot);
    }

    /// <summary>Called by UiInputController every frame with its own hit-test result -- null if the cursor isn't over a bound hotbar slot, or during an active drag (see UiInputController's own suppression). Also drives the summary popup (see UpdateSummary) every call, not just when candidateSlot itself changes -- MapViewState.ArmedSlot can change from elsewhere (see ActionTargetingController), and this is the only place ticked every frame that knows to check for it.</summary>
    public void UpdateHover(HotkeySlot? candidateSlot)
    {
        if (candidateSlot == _hoveredSlot)
        {
            _hoveredSlotFrames++;
        }
        else
        {
            _hoveredSlot = candidateSlot;
            _hoveredSlotFrames = candidateSlot is null ? 0 : 1;
        }

        mapViewState.HoverSlot = _hoveredSlotFrames >= HudChrome.HoverTooltipDelayFrames ? candidateSlot : null;

        UpdateSummary();
    }

    /// <summary>Shows/repositions/hides the Armed Hotkey Summary popup for whichever slot is currently hovered or armed (hover wins) -- moved here from the popup's own former per-frame Update override, since this method is already ticked every frame externally (see UpdateHover's own doc comment) and already has direct access to mapViewState/hotbarContent, the only two things that decision ever needed.</summary>
    private void UpdateSummary()
    {
        var slotToShow = mapViewState.HoverSlot ?? mapViewState.ArmedSlot;

        if (slotToShow == _displayedSummarySlot)
        {
            return;
        }

        _displayedSummarySlot = slotToShow;

        if (slotToShow is not { } slot || !hotbarContent.TryGetSlotSummary(slot, out var title, out var summary))
        {
            tooltipController.Hide(this);
            return;
        }

        tooltipController.Show(this, hotbarContent.GetSlotBounds(slot), PopupAnchor.North, PopupChrome.HotbarSummaryGap, SummaryMaximumSize, summary, title, useFixedWidth: true);
    }
}
