using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.Input;

/// <summary>The hotbar's taps and hover: a press and release on the same slot arms or confirms it, and the bound slot under the cursor shows its summary.</summary>
public sealed class HotbarInputHandler(HotbarController hotbarController) : IUiInputHandler
{
    /// <summary>Same reasoning/value as UiInputController's own tap thresholds -- small enough to absorb ordinary click jitter, comfortably smaller than an intentional drag.</summary>
    private const float HotbarTapThresholdPixels = 4f;

    /// <summary>Mouse position when the current hotbar-slot press started -- a release only counts as a tap within HotbarTapThresholdPixels of it.</summary>
    private Vector2 _hotbarPressMousePosition;

    /// <summary>Records which hotbar slot (if any) the press landed on, for OnRelease to compare against -- recorded on every press, so it never goes stale.</summary>
    public void OnPress(Element? pressedElement, Point position)
    {
        _hotbarPressMousePosition = new Vector2(position.X, position.Y);

        if (pressedElement is Window { Content: HotbarContent hotbarContent } &&
            hotbarContent.TryGetSlotAt(position, out var pressedSlot))
        {
            hotbarController.OnSlotPressed(pressedSlot);
        }
        else
        {
            hotbarController.OnPressOutsideHotbar();
        }
    }

    /// <summary>
    /// A press and release on the hotbar close enough together to be a tap, not a drag, is a tap on the released slot.
    /// HotbarController.OnSlotTapped itself checks that slot is the pressed one, so a drag that wanders back within the
    /// threshold over a different slot doesn't count.
    /// </summary>
    public void OnRelease(Element? releasedElement, Point position)
    {
        if (Vector2.Distance(_hotbarPressMousePosition, new Vector2(position.X, position.Y)) >= HotbarTapThresholdPixels)
        {
            return;
        }

        if (releasedElement is Window { Content: HotbarContent hotbarContent } &&
            hotbarContent.TryGetSlotAt(position, out var releasedSlot))
        {
            hotbarController.OnSlotTapped(releasedSlot);
        }
    }

    /// <summary>
    /// Feeds HotbarController.UpdateHover every frame with the bound hotbar slot (if any) under the cursor -- unbound slots
    /// never count, so hovering one doesn't blank a summary already showing for an armed slot. Only the hovered element
    /// counts, so a window drawn over the hotbar hides what's under it. Suppressed during a window move, resize or
    /// scrollbar drag, where a summary popping up would only be noise.
    /// </summary>
    public void OnPointerUpdate(Element? hoveredElement, Point position, bool isInteractionInProgress)
    {
        HotkeySlot? candidateSlot = null;

        if (!isInteractionInProgress &&
            hoveredElement is Window { Content: HotbarContent hotbarContent } &&
            hotbarContent.TryGetSlotAt(position, out var slot) &&
            hotbarContent.IsSlotBound(slot))
        {
            candidateSlot = slot;
        }

        hotbarController.UpdateHover(candidateSlot);
    }
}
