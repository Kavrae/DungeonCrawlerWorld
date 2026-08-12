using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Content;

namespace Presentation.UI;

/// <summary>
/// Owns the Armed Hotkey Summary window's lifecycle and turns "a press/release/hover happened on
/// the hotbar" into arm/activate/hover state changes -- a standalone manager class, not an
/// Element/Window itself, following the same relationship NotificationCenter has to its own
/// popups. UiInputController does all hit-testing (unchanged, its existing centralized job) and
/// forwards resolved hotbar-slot events here instead of containing this decision logic inline;
/// this keeps MapWindow (map rendering) and UiInputController (dispatch) from having to grow to
/// accommodate hotbar-specific state. A tap (OnSlotTapped) is deliberately a thin forward into
/// ActionTargetingController.HandleHotkeySlotPress rather than its own parallel decision tree --
/// see that method's own doc comment for why clicking and key-pressing a slot need to be the
/// exact same code path, not two implementations kept in sync by hand.
/// </summary>
public sealed class HotbarController(MapViewState mapViewState, HotbarContent hotbarContent, ActionTargetingController actionTargeting)
{
    private HotkeySlot? _pressedSlot;
    private HotkeySlot? _hoveredSlot;
    private int _hoveredSlotFrames;

    public void Initialize(ElementPoolService elementPoolService, FontService fontService, GlyphRenderer glyphRenderer, List<Element> dynamicHudWindows)
    {
        elementPoolService.RegisterFactory<ArmedHotkeySummaryWindow>((_, _) => new ArmedHotkeySummaryWindow(
            fontService, elementPoolService, glyphRenderer, mapViewState, hotbarContent));

        var summaryWindow = elementPoolService.CreateElement<ArmedHotkeySummaryWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = Vector2.Zero, // Repositioned every frame by ArmedHotkeySummaryWindow.Update once something's armed or hovered.
                MaximumSize = new Vector2(HotbarContent.SummaryWidth, 10000f), // Fixed width; effectively-unbounded height cap.
                DisplayMode = ElementDisplayMode.WrapContent,
                IsVisible = false,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        summaryWindow.Initialize();
        dynamicHudWindows.Add(summaryWindow);
    }

    /// <summary>Called by UiInputController.HandleMousePress when the press lands on a hotbar slot.</summary>
    public void OnSlotPressed(HotkeySlot slot) => _pressedSlot = slot;

    /// <summary>Called by UiInputController.HandleMousePress when the press lands anywhere else.</summary>
    public void OnPressOutsideHotbar() => _pressedSlot = null;

    /// <summary>
    /// Called by UiInputController.HandleMouseRelease once it's determined the release landed
    /// on a hotbar slot within the tap-distance threshold of the press -- ignored unless slot
    /// also matches whichever slot was actually pressed (OnSlotPressed), so a press-then-drag
    /// that happens to end up back within the tap threshold, but over a different slot, doesn't
    /// spuriously count as a tap on it. A confirmed tap is forwarded to ActionTargetingController.
    /// HandleHotkeySlotPress -- the same method the keyboard path calls -- so clicking a slot
    /// behaves exactly like pressing its key: arms an unarmed bound slot, confirms/re-presses an
    /// already-armed one, and shares that method's own double-tap window with the keyboard path.
    /// </summary>
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

    /// <summary>Called by UiInputController every frame with its own hit-test result -- null if the cursor isn't over a bound hotbar slot, or during an active drag (see UiInputController's own suppression).</summary>
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

        mapViewState.HoverSlot = _hoveredSlotFrames >= HudMetrics.HoverTooltipDelayFrames ? candidateSlot : null;
    }
}
