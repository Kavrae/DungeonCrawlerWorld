using Game.Modules.Abilities;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Content;

namespace Presentation.UI;

/// <summary>
/// Owns the Armed Hotkey Summary window's lifecycle and turns "a press/release/hover happened on
/// the hotbar" into arm/preview/hover state changes -- a standalone manager class, not an
/// Element/Window itself, following the same relationship NotificationCenter has to its own
/// popups. GameInputController does all hit-testing (unchanged, its existing centralized job) and
/// forwards resolved hotbar-slot events here instead of containing this decision logic inline;
/// this keeps MapWindow (map rendering) and GameInputController (dispatch) from having to grow to
/// accommodate hotbar-specific state.
/// </summary>
public sealed class HotbarController(MapViewState mapViewState, HotbarContent hotbarContent, AbilityTargetingController abilityTargeting)
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
                RelativePosition = Vector2.Zero, // Repositioned every frame by ArmedHotkeySummaryWindow.Update once something's armed/previewed/hovered.
                MaximumSize = new Vector2(HotbarContent.SummaryWidth, 10000f), // Fixed width; effectively-unbounded height cap.
                DisplayMode = ElementDisplayMode.WrapContent,
                IsVisible = false,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        summaryWindow.Initialize();
        dynamicHudWindows.Add(summaryWindow);
    }

    /// <summary>Called by GameInputController.HandleMousePress when the press lands on a hotbar slot.</summary>
    public void OnSlotPressed(HotkeySlot slot) => _pressedSlot = slot;

    /// <summary>Called by GameInputController.HandleMousePress when the press lands anywhere else -- an open preview closes immediately rather than waiting for release.</summary>
    public void OnPressOutsideHotbar()
    {
        _pressedSlot = null;
        mapViewState.PreviewSlot = null;
    }

    /// <summary>Called by GameInputController.HandleMouseRelease once it's determined the release
    /// landed on a hotbar slot within the tap-distance threshold of the press -- ignored unless
    /// slot also matches whichever slot was actually pressed (OnSlotPressed), so a press-then-drag
    /// that happens to end up back within the tap threshold, but over a different slot, doesn't
    /// spuriously count as a tap on it.</summary>
    public void OnSlotTapped(HotkeySlot slot)
    {
        var wasPressedSlot = _pressedSlot == slot;
        _pressedSlot = null;

        if (!wasPressedSlot)
        {
            return;
        }

        if (slot == mapViewState.ArmedSlot)
        {
            abilityTargeting.CancelArmedOrPendingAction();
            return;
        }

        if (!hotbarContent.TryGetSlotSummary(slot, out _, out _))
        {
            mapViewState.PreviewSlot = null; // Unbound slot -- nothing to preview.
            return;
        }

        mapViewState.PreviewSlot = mapViewState.PreviewSlot == slot ? null : slot;
    }

    /// <summary>Called by GameInputController every frame with its own hit-test result -- null if the cursor isn't over a bound hotbar slot, or during an active drag (see GameInputController's own suppression).</summary>
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
