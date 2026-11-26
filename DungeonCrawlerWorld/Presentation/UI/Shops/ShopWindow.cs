using Game.Spawning;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using UIEngine.Input;
using UIEngine.UI.Chrome;
using UIEngine.UI;

namespace Presentation.UI.Shops;

/// <summary>The shop side of trading: its name and description above a grid of its priced items.</summary>
/// <remarks>
/// Its grid shows the shop's view (GridViewSettings.Shop, switched by the player's Inventory window while the shop is open):
/// in Card view priced ShopItemStackCells two a row, in Icon view as many squares as fit the same width. A shop holds
/// any number of stacks (UnlimitedStackCountComponent): the header shows its count out of ∞, and past MaximumGridRows the grid scrolls
/// (ContainerInventoryWindow has the shared layout rules). A drop anywhere its grid and currency row leave unanswered
/// lands in the shop's inventory.
/// </remarks>
public sealed class ShopWindow(
    ElementServices services,
    InventoryServices inventoryServices,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState,
    EntityNaming entityNaming)
    : ContainerInventoryWindow(services, inventoryServices, world, contextMenuController, mapViewState), IContentDropTarget
{
    private InventoryDropTarget? _dropTarget;

    private static readonly float CardWidth = InventoryGridContent.CellSize.X * InventoryGridContent.ShopCellWidthMultiplier;

    /// <summary>Two cards a row -- a scannable list, tall rather than wide.</summary>
    private const int CardColumns = 2;

    private static readonly float CardColumnsWidth = CardColumns * CardWidth + (CardColumns - 1) * InventoryGridContent.CellGap;

    /// <summary>As many squares as fit the two cards' width.</summary>
    private static readonly int IconColumns = (int)((CardColumnsWidth + InventoryGridContent.CellGap) / (InventoryGridContent.CellSize.X + InventoryGridContent.CellGap));

    protected override int GridColumns => GridViewSetting.ShowsCards ? CardColumns : IconColumns;

    /// <summary>Two cards and a scrollbar's room, so the window keeps its width whether or not its list scrolls, and in either view.</summary>
    protected override float? FixedWindowWidth => CardColumnsWidth + WindowChrome.ScrollbarThickness + ContentInset.X;

    protected override float GridCellWidth => GridViewSetting.ShowsCards ? CardWidth : InventoryGridContent.CellSize.X;

    /// <summary>The shop's view (GridViewSettings.Shop), which the player's inventory beside it follows.</summary>
    protected override GridViewSetting GridViewSetting => InventoryServices.GridViews.Shop;

    protected override int MinimumGridRows => 5;

    /// <summary>Twelve rows before the list scrolls, short enough to stay on screen beside the inventory.</summary>
    protected override int MaximumGridRows => 12;

    protected override int? StackCapacity => null;

    protected override IReadOnlyList<string> BuildSummaryLines() => [entityNaming.NameOf(EntityId), entityNaming.DescriptionOf(EntityId)];

    protected override void OnConfigured() => _dropTarget = new InventoryDropTarget(EntityId);

    public object? DropTargetFor(object payload, Point position) => _dropTarget;
}
