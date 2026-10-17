using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.Input;

/// <summary>The refusal cursor over something the player tried to use and can't right now.</summary>
/// <remarks>
/// A hotbar slot that's disabled (HotbarContent.IsSlotActive -- blocked or locked), or the map while the armed action or
/// item can't be confirmed right now (MapWindow.IsArmedConfirmRefused -- not ready before the input buffer would expire).
/// Asks only the hovered element, so whatever is drawn over the hotbar or the map takes the cursor instead.
/// </remarks>
public sealed class ActivationRefusalInputHandler : IUiInputHandler
{
    public bool IsInputRefusedAt(Element? hoveredElement, Point position) => hoveredElement switch
    {
        Window { Content: HotbarContent hotbarContent } => hotbarContent.TryGetSlotAt(position, out var slot) && !hotbarContent.IsSlotActive(slot),
        MapWindow mapWindow => mapWindow.IsArmedConfirmRefused,
        _ => false,
    };
}
