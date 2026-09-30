using Engine.ECS.Components;
using Engine.Events;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Lootboxes;

namespace Tests.Presentation;

/// <summary>The loot box results window's content, hosted in a real window and driven through UiInputController.</summary>
[TestClass]
[DoNotParallelize]
public sealed class LootboxResultsContentTests
{
    private const int PlayerEntityId = 1;
    private const int CorpseEntityId = 2;

    private static readonly KeyboardState NoKeys = new();

    private static readonly ItemDefinition Potion = new(Guid.NewGuid(), "Potion", SpriteName: null, Glyph: "p", Color.White, Tags: [Tag.Potion], Effects: [], Summary: "Drink it.");
    private static readonly ItemDefinition Scroll = new(Guid.NewGuid(), "Scroll", SpriteName: null, Glyph: "s", Color.White, Tags: [Tag.Scroll], Effects: []);

    private static MouseState MouseAt(Point point, ButtonState leftButton) =>
        new(point.X, point.Y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private sealed record Harness(UiInputController Controller, Window ResultsWindow, Window CorpseGridWindow, LootboxResultsContent Content, ComponentManager ComponentManager, UiLayerStack Layers, List<GrantedItem> ClickedRewards)
    {
        public List<InventoryItemStackCell> Cells => [.. ResultsWindow.ChildElements.OfType<InventoryItemStackCell>()];

        public List<TextWindow> Headers => [.. ResultsWindow.ChildElements.OfType<TextWindow>()];
    }

    private static Harness Build(IReadOnlyList<OpenedLootboxGroup>? groups = null)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10));

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(Potion);
        itemCatalog.Register(Scroll);
        var lootboxCatalog = new LootboxCatalog(itemCatalog);
        foreach (var type in LootboxTypes.All)
        {
            lootboxCatalog.Register(type);
        }

        var potionStackId = InventoryActions.AddItem(componentManager, PlayerEntityId, Potion.Id, quantity: 7);
        var scrollStackId = InventoryActions.AddItem(componentManager, PlayerEntityId, Scroll.Id, quantity: 2);
        groups ??=
        [
            new OpenedLootboxGroup(new LootboxKind(LootboxTypes.Alchemist.Id, LootboxRarity.Bronze), 1, [new GrantedItem(Potion.Id, 4, potionStackId)]),
            new OpenedLootboxGroup(new LootboxKind(LootboxTypes.Adventurer.Id, LootboxRarity.Silver), 3, [new GrantedItem(Potion.Id, 3, potionStackId), new GrantedItem(Scroll.Id, 2, scrollStackId)]),
        ];

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));

        var layers = new UiLayerStack();
        var tooltipController = new TooltipController();
        tooltipController.Initialize(windowService, layers);

        var world = TestWorlds.Create(new Game.World.Map(new Engine.Math.Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, layers);
        var clickedRewards = new List<GrantedItem>();

        var content = new LootboxResultsContent(groups, PlayerEntityId, lootboxCatalog, itemCatalog, tooltipController, clickedRewards.Add);
        var resultsWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(400, 400), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false, CanUserScrollVertical = true },
        });
        resultsWindow.SetContent(content);
        resultsWindow.Initialize();
        layers.Add(UiLayer.Base, resultsWindow);

        var corpseGridWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(600, 0), Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        corpseGridWindow.SetContent(new InventoryGridContent(world, TestInventoryServices.Over(componentManager, itemCatalog, world), windowService, contextMenuController, CorpseEntityId, filterTag: null, tooltipController, static () => null, new MapViewState(), static (_, _) => { }, static (_, _) => { }, static (_, _) => { }, static _ => { }, simulationClock: new Engine.ECS.Systems.SimulationClock()));
        corpseGridWindow.Initialize();
        layers.Add(UiLayer.Base, corpseGridWindow);

        var controller = TestUiInputController.Create(layers, new Vector2(2000, 2000), componentManager, world, new EventBus(), itemCatalog, contextMenuController: contextMenuController);
        return new Harness(controller, resultsWindow, corpseGridWindow, content, componentManager, layers, clickedRewards);
    }

    [TestMethod]
    public void EachGroup_HasAHeaderInItsRarityColor_WithACountOnlyWhenMoreThanOneBoxOpened()
    {
        var harness = Build();

        CollectionAssert.AreEqual(new[] { "Bronze Alchemist Box", "Silver Adventurer Box x3" }, harness.Headers.Select(header => header.OriginalText).ToArray());
        Assert.AreEqual(LootboxRarityColors.For(LootboxRarity.Bronze), harness.Headers[0].TextColor);
        Assert.AreEqual(LootboxRarityColors.For(LootboxRarity.Silver), harness.Headers[1].TextColor);
    }

    [TestMethod]
    public void EachReward_IsACellShowingWhatItsGroupGranted_BelowItsHeader()
    {
        var harness = Build();

        var cells = harness.Cells;
        Assert.HasCount(3, cells);
        CollectionAssert.AreEqual(new[] { Potion.Id, Potion.Id, Scroll.Id }, cells.Select(cell => cell.ItemDefinitionId).ToArray());
        Assert.IsGreaterThanOrEqualTo(harness.Headers[0].Rectangle.Bottom, cells[0].Rectangle.Top);
        Assert.IsGreaterThanOrEqualTo(harness.Headers[1].Rectangle.Bottom, cells[1].Rectangle.Top);
        Assert.IsLessThanOrEqualTo(harness.Headers[1].Rectangle.Top, cells[0].Rectangle.Bottom);
    }

    [TestMethod]
    public void ClickingAReward_HandsItsGrantedItemToTheCallback()
    {
        var harness = Build();
        var scrollCell = harness.Cells[2];
        var point = scrollCell.Rectangle.Center;

        harness.Controller.Update(NoKeys, MouseAt(point, ButtonState.Released));
        harness.Controller.Update(NoKeys, MouseAt(point, ButtonState.Pressed));
        harness.Controller.Update(NoKeys, MouseAt(point, ButtonState.Released));

        Assert.HasCount(1, harness.ClickedRewards);
        Assert.AreEqual(Scroll.Id, harness.ClickedRewards[0].ItemDefinitionId);
        Assert.AreEqual(2, harness.ClickedRewards[0].Quantity);
    }

    [TestMethod]
    public void DraggingAReward_OntoAnotherEntitysGrid_MovesNothing()
    {
        var harness = Build();
        var pressPoint = harness.Cells[0].Rectangle.Center;

        harness.Controller.Update(NoKeys, MouseAt(pressPoint, ButtonState.Released));
        harness.Controller.Update(NoKeys, MouseAt(pressPoint, ButtonState.Pressed));
        harness.Controller.Update(NoKeys, MouseAt(harness.CorpseGridWindow.ContentRectangle.Center, ButtonState.Released));

        var stacks = harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(2, stacks.CountForEntity(PlayerEntityId));
        Assert.AreEqual(0, stacks.CountForEntity(CorpseEntityId));
        Assert.IsTrue(harness.Cells.All(cell => !cell.IsDragSource));
    }

    [TestMethod]
    public void NarrowingTheWindow_ReflowsTheRewardsIntoMoreRows()
    {
        var manyRewards = Enumerable.Range(0, 10).Select(_ => new GrantedItem(Potion.Id, 1, 0)).ToList();
        var harness = Build([new OpenedLootboxGroup(new LootboxKind(LootboxTypes.Weapon.Id, LootboxRarity.Gold), 1, manyRewards)]);
        var rowsBefore = harness.Cells.Select(cell => cell.Rectangle.Top).Distinct().Count();

        harness.ResultsWindow.SetSize(new Vector2(150, 400));

        var rowsAfter = harness.Cells.Select(cell => cell.Rectangle.Top).Distinct().Count();
        Assert.IsGreaterThan(rowsBefore, rowsAfter);
        Assert.HasCount(10, harness.Cells);
    }
}
