using Game.Modules.Actions;

namespace Presentation.UI;

/// <summary>Whether using a toggle arms it first: turning one on in Ground mode does, since that puts something on the player's tile (an item is put down, an action's aura anchored there), so it never happens on a single careless press.</summary>
/// <remarks>Turning one on in Target mode, and turning any off, happens at once. One rule for the hotbar, the inventory and their menus' words (ActivationMenuLabels).</remarks>
public static class ToggleArming
{
    public static bool ArmsFirst(bool isToggledOn, TargetingMode mode) => !isToggledOn && mode == TargetingMode.Ground;
}
