namespace Presentation.Input.DragDrop;

/// <summary>An entity's inventory and currency, as what a content drop lands on -- named by its grid, its currency row, or a whole window showing it (InventoryManagementWindow, ShopWindow, a TradeWindow column).</summary>
/// <remarks>A class, kept by whatever names it, so the drop target asked for every frame of a drag allocates nothing.</remarks>
public sealed record InventoryDropTarget(int EntityId);
