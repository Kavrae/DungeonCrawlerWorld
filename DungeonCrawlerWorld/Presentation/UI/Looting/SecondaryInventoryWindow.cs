using Game.Modules.Inventory;
using Game.Spawning;
using Game.Views;
using Game.World;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Looting;

/// <summary>The non-player side of looting -- a corpse, or a container such as a treasure chest (lootable while alive) -- with its name, killer and death tick above a five-column grid.</summary>
/// <remarks>
/// No tabs, sort or filter (TODO.md "Corpse looting"). Holds at most InventoryCapacity.MaxNonPlayerStackCount stacks,
/// which the header counts against and the grid's four rows fit (ContainerInventoryWindow has the shared layout rules).
/// </remarks>
public sealed class SecondaryInventoryWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    UiRenderService renderService,
    InventoryServices inventoryServices,
    World world,
    ContextMenuController contextMenuController,
    MapViewState mapViewState,
    Engine.ECS.Systems.SimulationClock simulationClock,
    EntityNaming entityNaming,
    HealthView healthView)
    : ContainerInventoryWindow(fontService, elementPoolService, renderService, inventoryServices, world, contextMenuController, mapViewState, simulationClock)
{
    /// <summary>Five plain cells, the "2x5" shape a ten-item corpse reads as.</summary>
    protected override int GridColumns => 5;

    protected override float GridCellWidth => InventoryGridContent.CellSize.X;

    protected override int MinimumGridRows => 2;

    protected override int MaximumGridRows => InventoryCapacity.MaxNonPlayerStackCount / GridColumns;

    protected override int? StackCapacity => InventoryCapacity.MaxNonPlayerStackCount;

    protected override int SummaryLineCount => 3;

    protected override IReadOnlyList<string> BuildSummaryLines() =>
        [entityNaming.NameOf(EntityId), $"Slain by: {ResolveKillerName()}", $"Died at tick: {ResolveDiedAtFrame()}"];

    private string ResolveKillerName()
    {
        if (!healthView.TryGetDeath(EntityId, out var death) || death.KilledBy is not { Kind: ActionSourceKind.Entity } killer)
        {
            return "Unknown";
        }

        return killer.Identity.DisplayName;
    }

    private long ResolveDiedAtFrame() => healthView.TryGetDeath(EntityId, out var death) ? death.DiedAtFrame : 0;
}
