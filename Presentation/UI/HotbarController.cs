using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Content;

namespace Presentation.UI;

/// <summary>Manages the lifecycle of the Armed Hotkey Summary window and handles hotbar slot interactions.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class HotbarController(MapViewState mapViewState, HotbarContent hotbarContent, ActionTargetingController actionTargeting)
{
    private HotkeySlot? _pressedSlot;
    private HotkeySlot? _hoveredSlot;
    private int _hoveredSlotFrames;

    /// <summary>Initializes the hotbar controller with the specified services and windows.</summary>
    /// <param name="elementPoolService">The service for managing UI element pools.</param>
    /// <param name="fontService">The service for handling fonts.</param>
    /// <param name="glyphRenderer">The renderer for drawing glyphs.</param>
    /// <param name="dynamicHudWindows">The list of dynamic HUD windows.</param>
    public void Initialize(ElementPoolService elementPoolService, FontService fontService, GlyphRenderer glyphRenderer, List<Element> dynamicHudWindows)
    {
        elementPoolService.RegisterFactory<ArmedHotkeySummaryWindow>(() => new ArmedHotkeySummaryWindow(
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
