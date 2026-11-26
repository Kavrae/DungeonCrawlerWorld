using Engine.ECS.Components;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>The cell a right-click menu is open for is highlighted while the menu is open, follows its stack when the grid rebuilds its cells, and ends with the menu; a cell bound to a hotbar slot shows its key.</summary>
[TestClass]
[DoNotParallelize]
public sealed class InventoryGridContextMenuTargetTests
{
    private const int PlayerEntityId = 1;
    private static readonly Guid FirstItemId = Guid.NewGuid();
    private static readonly Guid SecondItemId = Guid.NewGuid();
    private static readonly Guid ThirdItemId = Guid.NewGuid();

    private sealed record Fixture(InventoryGridContent Grid, Window HostWindow, ComponentManager ComponentManager, ContextMenuController ContextMenuController, Func<(InventoryGridContent Grid, Window HostWindow)> AddGrid, ElementPoolService Pool);

    /// <summary>Stands in for a hotbar slot bound to one stack.</summary>
    private sealed class ShowsOneStack(ElementServices services, int entityId, uint stackInstanceId) : Element(services), IShowsItemStacks
    {
        public bool ShowsStack(int shownEntityId, uint shownStackInstanceId) => shownEntityId == entityId && shownStackInstanceId == stackInstanceId;
    }

    private static Fixture Build(bool addItems = true)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));

        var fontService = TestFonts.Shared;
        var windowService = TestElementPoolServiceFactory.Create(fontService);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver);
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(TestElementServices.For(windowService, fontService), spriteSheetService));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(windowService, fontService)));

        var world = TestWorlds.Create(new Game.World.Map(new Engine.Math.Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(FirstItemId, "Apple", null, "a", Color.White, Tags: [], Effects: []));
        itemCatalog.Register(new ItemDefinition(SecondItemId, "Bread", null, "b", Color.White, Tags: [], Effects: []));
        itemCatalog.Register(new ItemDefinition(ThirdItemId, "Cheese", null, "c", Color.White, Tags: [], Effects: []));

        var tooltipController = new TooltipController();
        tooltipController.Initialize(windowService, new UiLayerStack());

        var inventoryServices = TestInventoryServices.Over(componentManager, itemCatalog, world);

        (InventoryGridContent, Window) AddGrid()
        {
            var grid = new InventoryGridContent(world, inventoryServices, windowService, contextMenuController, PlayerEntityId, filterTag: Engine.Tags.GameplayTag.None, static () => null, new MapViewState(), static (_, _) => { }, static (_, _) => { }, static _ => { });
            var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
                Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
            });
            hostWindow.ContentPadding = Vector2.Zero;
            hostWindow.Initialize();
            grid.Initialize(hostWindow);
            return (grid, hostWindow);
        }

        var (firstGrid, firstHostWindow) = AddGrid();
        if (addItems)
        {
            InventoryActions.AddItem(componentManager, PlayerEntityId, FirstItemId, quantity: 1);
            InventoryActions.AddItem(componentManager, PlayerEntityId, SecondItemId, quantity: 1);
        }

        firstGrid.Update(new GameTime());

        return new Fixture(firstGrid, firstHostWindow, componentManager, contextMenuController, AddGrid, windowService);
    }

    private static ShowsOneStack StandInSlot(Fixture fixture, uint stackInstanceId) =>
        new(TestElementServices.For(fixture.Pool, TestFonts.Shared), PlayerEntityId, stackInstanceId);

    private static InventoryItemStackCell CellOf(Fixture fixture, Guid itemDefinitionId) =>
        fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single(cell => cell.ItemDefinitionId == itemDefinitionId);

    private static IEnumerable<InventoryItemStackCell> Targets(Fixture fixture) =>
        fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Where(cell => cell.IsContextMenuTarget);

    [TestMethod]
    public void RightClickingACell_HighlightsItAlone_UntilTheMenuCloses()
    {
        var fixture = Build();

        CellOf(fixture, SecondItemId).OnRightClicked!.Invoke(Point.Zero);
        fixture.Grid.Update(new GameTime());

        Assert.AreSame(CellOf(fixture, SecondItemId), Targets(fixture).Single());

        fixture.ContextMenuController.Close();
        fixture.Grid.Update(new GameTime());

        Assert.IsEmpty(Targets(fixture));
    }

    [TestMethod]
    public void TheGridRebuildingWhileTheMenuIsOpen_KeepsTheHighlightOnTheSameStack()
    {
        var fixture = Build();
        CellOf(fixture, SecondItemId).OnRightClicked!.Invoke(Point.Zero);

        InventoryActions.AddItem(fixture.ComponentManager, PlayerEntityId, ThirdItemId, quantity: 1);
        fixture.Grid.Update(new GameTime());

        Assert.AreEqual(SecondItemId, Targets(fixture).Single().ItemDefinitionId);
        Assert.IsTrue(fixture.ContextMenuController.IsOpenFor(CellOf(fixture, SecondItemId)));
    }

    [TestMethod]
    public void AMenuOpenForAnythingShowingAStack_HighlightsTheCellOfIt()
    {
        var fixture = Build();
        var slot = StandInSlot(fixture, CellOf(fixture, FirstItemId).StackInstanceId!.Value);

        fixture.ContextMenuController.Open(Vector2.Zero, [new DropdownOption("Unbind", null, true, static () => { })], slot);
        fixture.Grid.Update(new GameTime());

        Assert.AreSame(CellOf(fixture, FirstItemId), Targets(fixture).Single());
    }

    [TestMethod]
    public void RightClickingACell_HighlightsTheSameStackInEveryOtherGrid()
    {
        var fixture = Build();
        var (otherGrid, otherHostWindow) = fixture.AddGrid();
        otherGrid.Update(new GameTime());

        CellOf(fixture, SecondItemId).OnRightClicked!.Invoke(Point.Zero);
        otherGrid.Update(new GameTime());

        var highlighted = otherHostWindow.ChildElements.OfType<InventoryItemStackCell>().Where(cell => cell.IsContextMenuTarget).Single();
        Assert.AreEqual(SecondItemId, highlighted.ItemDefinitionId);
        Assert.AreNotSame(CellOf(fixture, SecondItemId), highlighted);
    }

    [TestMethod]
    public void AMergedStack_IsHighlightedForAMenuAboutAnyMember_AndShowsEveryMember()
    {
        var fixture = Build(addItems: false);
        var plainStack = InventoryActions.AddItem(fixture.ComponentManager, PlayerEntityId, FirstItemId, quantity: 1);
        var divergentStack = InventoryActions.AddDivergentItem(fixture.ComponentManager, PlayerEntityId, new ItemDefinition(FirstItemId, "Apple", "bruised", "a", Color.White, Tags: [], Effects: []));
        fixture.Grid.Update(new GameTime());
        var merged = CellOf(fixture, FirstItemId);
        Assert.IsNull(merged.StackInstanceId, "The two stacks share one Merged Stack cell.");

        Assert.IsTrue(merged.ShowsStack(PlayerEntityId, plainStack));
        Assert.IsTrue(merged.ShowsStack(PlayerEntityId, divergentStack));

        fixture.ContextMenuController.Open(Vector2.Zero, [new DropdownOption("Unbind", null, true, static () => { })], StandInSlot(fixture, divergentStack));
        fixture.Grid.Update(new GameTime());

        Assert.IsTrue(merged.IsContextMenuTarget);
    }

    [TestMethod]
    public void TheTargetsStackGoingWhileTheMenuIsOpen_HighlightsNothing()
    {
        var fixture = Build();
        var cell = CellOf(fixture, SecondItemId);
        cell.OnRightClicked!.Invoke(Point.Zero);

        InventoryActions.RemoveOneUnit(fixture.ComponentManager, PlayerEntityId, cell.StackInstanceId!.Value);
        fixture.Grid.Update(new GameTime());

        Assert.IsEmpty(Targets(fixture));
    }

    [TestMethod]
    public void ACellWhoseStackIsBoundToAKeyedSlot_ShowsTheKey_AndLosesItWhenUnbound()
    {
        var fixture = Build();
        var apple = CellOf(fixture, FirstItemId);
        var bindings = fixture.ComponentManager.GetMultiPool<Game.Modules.Inventory.Components.ItemHotkeyBindingComponent>();
        bindings.Add(PlayerEntityId, new Game.Modules.Inventory.Components.ItemHotkeyBindingComponent(Game.Modules.Actions.HotkeySlot.Base2, apple.StackInstanceId!.Value));

        fixture.Grid.Update(new GameTime());
        Assert.AreEqual("E", apple.KeyLabel);
        Assert.IsNull(CellOf(fixture, SecondItemId).KeyLabel);

        bindings.Remove(PlayerEntityId);
        fixture.Grid.Update(new GameTime());
        Assert.IsNull(apple.KeyLabel);
    }
}
