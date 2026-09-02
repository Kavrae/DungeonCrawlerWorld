namespace Presentation.UI.Content;

/// <summary>
/// Implemented by whichever content class owns an entity's inventory-adjacent drop surface --
/// InventoryGridContent (item stacks) and CurrencyRowContent (Gold/Credits) -- and set as the
/// hosting Window's own Tag (see InventoryGridContent.Initialize/CurrencyRowContent.Build). Lets
/// UiInputController's content-drag drop resolution (FindDropTargetEntityId) find "which entity
/// owns whatever's under the drop point" with one shared walk regardless of whether the drop
/// landed on a grid cell or a currency element -- an item dropped on a currency row, or a currency
/// element dropped on a grid, both resolve to the same destination entity this way.
/// </summary>
public interface IInventoryDropTarget
{
    int EntityId { get; }
}
