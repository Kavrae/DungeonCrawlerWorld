using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
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

namespace Tests.Presentation;

/// <summary>A loot box in the player's inventory, driven through the real grid and UiInputController: it opens on "Open All" or double-click, offers nothing else, and never starts a drag.</summary>
[TestClass]
[DoNotParallelize]
public sealed class LootboxInventoryInputTests
{
    private const int PlayerEntityId = 1;
    private const int CorpseEntityId = 2;

    private static readonly KeyboardState NoKeys = new();

    private static MouseState MouseAt(Point point, ButtonState leftButton) =>
        new(point.X, point.Y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private sealed record Harness(
        UiInputController Controller,
        Window PlayerGridWindow,
        Window CorpseGridWindow,
        InventoryItemStackCell LootboxCell,
        ComponentManager ComponentManager,
        ContextMenuController ContextMenuController,
        List<int> OpenRequests);

    private static Harness Build()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10));

        var itemCatalog = new ItemCatalog();
        var lootboxCatalog = new LootboxCatalog(itemCatalog);
        itemCatalog.AddDefinitionSource(lootboxCatalog);
        lootboxCatalog.Register(LootboxTypes.Adventurer);
        LootboxActions.Grant(componentManager, lootboxCatalog, new EventBus(), PlayerEntityId, new LootboxReward(LootboxTypes.Adventurer.Id, LootboxRarity.Bronze));

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));
        windowService.RegisterFactory<ContextMenu>(() => new ContextMenu(fontService, windowService, labelRenderer));
        var tooltipController = new TooltipController();
        tooltipController.Initialize(windowService, new UiLayerStack());

        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var layers = new UiLayerStack();
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, layers);
        var mapViewState = new MapViewState();
        var openRequests = new List<int>();

        Window BuildGridWindow(int entityId, Vector2 position)
        {
            var window = windowService.CreateElement<Window>(null, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
                Layout = new ElementLayoutOptions { RelativePosition = position, Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
            });
            window.SetContent(new InventoryGridContent(world, TestInventoryServices.Over(componentManager, itemCatalog, world), windowService, contextMenuController, entityId, filterTag: Engine.Tags.GameplayTag.None, tooltipController, () => CorpseEntityId, mapViewState, static (_, _) => { }, static (_, _) => { }, static (_, _) => { }, openRequests.Add, simulationClock: new SimulationClock()));
            window.Initialize();
            return window;
        }

        var playerGridWindow = BuildGridWindow(PlayerEntityId, new Vector2(0, 0));
        var corpseGridWindow = BuildGridWindow(CorpseEntityId, new Vector2(500, 0));
        foreach (var window in new[] { playerGridWindow, corpseGridWindow })
        {
            layers.Add(UiLayer.Base, window);
        }

        var controller = TestUiInputController.Create(layers, new Vector2(2000, 2000), componentManager, world, new EventBus(), itemCatalog, contextMenuController: contextMenuController, mapViewState: mapViewState);
        var lootboxCell = playerGridWindow.ChildElements.OfType<InventoryItemStackCell>().Single();

        return new Harness(controller, playerGridWindow, corpseGridWindow, lootboxCell, componentManager, contextMenuController, openRequests);
    }

    private static void Click(UiInputController controller, Point point)
    {
        controller.Update(NoKeys, MouseAt(point, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(point, ButtonState.Released));
    }

    [TestMethod]
    public void LootboxCell_CanNeitherBeTradedNorBoundToTheHotbar()
    {
        var harness = Build();

        Assert.IsFalse(harness.LootboxCell.CanTrade);
        Assert.IsFalse(harness.LootboxCell.CanBindToHotbar);
    }

    [TestMethod]
    public void RightClick_PlayerLootbox_OffersOnlyOpenAll_WhichRequestsOpeningForThePlayer()
    {
        var harness = Build();

        harness.LootboxCell.OnRightClicked!.Invoke(Point.Zero);

        var options = harness.ContextMenuController.Menu.ChildElements.OfType<Button>().ToList();
        CollectionAssert.AreEqual(new[] { "Open All" }, options.Select(option => option.LeftText).ToArray());

        Click(harness.Controller, options[0].Rectangle.Center);

        CollectionAssert.AreEqual(new[] { PlayerEntityId }, harness.OpenRequests);
    }

    [TestMethod]
    public void DoubleClick_PlayerLootbox_RequestsOpeningForThePlayer()
    {
        var harness = Build();
        var point = harness.LootboxCell.ContentRectangle.Center;
        harness.Controller.Update(NoKeys, MouseAt(point, ButtonState.Released));

        Click(harness.Controller, point);
        Click(harness.Controller, point);

        CollectionAssert.AreEqual(new[] { PlayerEntityId }, harness.OpenRequests);
    }

    [TestMethod]
    public void Drag_PlayerLootboxOntoAnotherEntitysGrid_LeavesItWhereItIs()
    {
        var harness = Build();
        var pressPoint = harness.LootboxCell.ContentRectangle.Center;

        harness.Controller.Update(NoKeys, MouseAt(pressPoint, ButtonState.Released));
        harness.Controller.Update(NoKeys, MouseAt(pressPoint, ButtonState.Pressed));
        harness.Controller.Update(NoKeys, MouseAt(harness.CorpseGridWindow.ContentRectangle.Center, ButtonState.Released));

        var stacks = harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, stacks.CountForEntity(PlayerEntityId));
        Assert.AreEqual(0, stacks.CountForEntity(CorpseEntityId));
    }
}
