using Game.Spawning;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Shops;

/// <summary>The shop side of trading: its name and description above a two-column grid of priced items.</summary>
/// <remarks>
/// Always in shop mode while open (MapViewState.OpenShopEntityId), so its grid builds ShopItemStackCells, each
/// InventoryGridContent.ShopCellWidthMultiplier plain cells wide -- a scannable list, tall rather than wide. A shop holds
/// any number of stacks (UnlimitedStackCountComponent): the header shows its count out of ∞, and past MaximumGridRows the grid scrolls
/// (ContainerInventoryWindow has the shared layout rules). A drop anywhere its grid and currency row leave unanswered
/// lands in the shop's inventory.
/// </remarks>
public sealed class ShopWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    UiRenderService renderService,
    InventoryServices inventoryServices,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState,
    Engine.ECS.Systems.SimulationClock simulationClock,
    EntityNaming entityNaming)
    : ContainerInventoryWindow(fontService, elementPoolService, renderService, inventoryServices, world, contextMenuController, mapViewState, simulationClock), IContentDropTarget
{
    private InventoryDropTarget? _dropTarget;

    protected override int GridColumns => 2;

    protected override float GridCellWidth => InventoryGridContent.CellSize.X * InventoryGridContent.ShopCellWidthMultiplier;

    protected override int MinimumGridRows => 5;

    /// <summary>Twelve rows of two -- 24 items -- before the list scrolls, short enough to stay on screen beside the inventory.</summary>
    protected override int MaximumGridRows => 12;

    protected override int? StackCapacity => null;

    protected override int SummaryLineCount => 2;

    protected override IReadOnlyList<string> BuildSummaryLines() => [entityNaming.NameOf(EntityId), entityNaming.DescriptionOf(EntityId)];

    protected override void OnConfigured() => _dropTarget = new InventoryDropTarget(EntityId);

    public object? DropTargetFor(object payload, Point position) => _dropTarget;
}
