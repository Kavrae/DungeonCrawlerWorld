using Game.Modules.Actions;
using Game.Modules.Actions.Activators;

namespace Presentation.UI;

/// <summary>Whether a toggle is on, in text, the same wherever it is described: "Active" while it is on.</summary>
/// <remarks>Used by the inventory tooltip, the hotbar summary and Item Details. What a toggle holds while on is its definition's Effects, which each of those already lists; what it takes to turn on and keep on is CostText's.</remarks>
public static class ToggleText
{
    public const string Active = "Active";

    /// <summary>Whether an item definition is a lit unit of a toggle item.</summary>
    public static bool IsLit(ActivatableDefinition definition) => definition is Game.Modules.Inventory.ItemDefinition { Activator: ToggleItemActivator { IsToggledOn: true } };
}
