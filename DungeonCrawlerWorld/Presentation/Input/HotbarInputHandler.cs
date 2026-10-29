using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.Input;

/// <summary>The hotbar's hover: the bound slot under the cursor shows its summary.</summary>
/// <remarks>A slot's click is its own (HotbarSlotElement); this only ticks the summary every frame, which must run whether or not the cursor is over the hotbar.</remarks>
public sealed class HotbarInputHandler(HotbarController hotbarController) : IUiInputHandler
{
    /// <summary>
    /// Feeds HotbarController.UpdateHover every frame with the bound hotbar slot (if any) under the cursor -- unbound slots
    /// never count, so hovering one doesn't blank a summary already showing for an armed slot. Only the hovered element
    /// counts, so a window drawn over the hotbar hides what's under it. Suppressed during a window move, resize or
    /// scrollbar drag, where a summary popping up would only be noise.
    /// </summary>
    public void OnPointerUpdate(Element? hoveredElement, Point position, bool isInteractionInProgress)
    {
        HotkeySlot? candidateSlot = !isInteractionInProgress && hoveredElement is HotbarSlotElement { IsBound: true } slotElement
            ? slotElement.Slot
            : null;

        hotbarController.UpdateHover(candidateSlot);
    }
}
