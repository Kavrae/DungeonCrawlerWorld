using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.ItemPiles;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Inventory;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>Arming a drop and placing it, through the real ActionTargetingController, and where the item windows offer Drop.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ItemDropPresentationTests
{
    private const int PlayerEntityId = TestMapWindows.PlayerEntityId;
    private static readonly Vector3Int PlayerPosition = new(20, 20, 0);
    private static readonly Vector3Int AdjacentTile = new(21, 21, 0);
    private static readonly Vector3Int TwoTilesAway = new(22, 20, 0);
    private static readonly Guid PotionItemId = Guid.NewGuid();

    private sealed record MapFixture(TestMapWindows.MapWindowHarness Map, uint StackInstanceId, RecordingDropPlacement Placement)
    {
        public ActionTargetingController Targeting => Map.ActionTargetingController;

        public MapViewState State => Map.MapViewState;

        public List<(ItemDrop Drop, Vector3Int Tile)> ConfirmedDrops => Placement.ConfirmedDrops;
    }

    /// <summary>Takes a drop on any tile but TwoTilesAway, recording every one it is asked to place.</summary>
    private sealed class RecordingDropPlacement : IItemDropPlacement
    {
        public List<(ItemDrop Drop, Vector3Int Tile)> ConfirmedDrops { get; } = [];

        public bool CanDropAt(ItemDrop drop, Vector3Int tile) => tile != TwoTilesAway;

        public bool TryDropAt(ItemDrop drop, Vector3Int tile)
        {
            ConfirmedDrops.Add((drop, tile));
            return CanDropAt(drop, tile);
        }
    }

    private static MapFixture BuildMap()
    {
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [], Effects: []));
        var placement = new RecordingDropPlacement();
        var map = TestMapWindows.Create(60, 60, 1, PlayerPosition, itemCatalog: itemCatalog, dropPlacement: placement);
        map.ComponentManager.Merge(PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));
        var stackInstanceId = InventoryActions.AddItem(map.ComponentManager, PlayerEntityId, PotionItemId, quantity: 3);
        return new MapFixture(map, stackInstanceId, placement);
    }

    [TestMethod]
    public void ArmingADrop_HighlightsThePlayersTileAndTheEightAroundIt()
    {
        var fixture = BuildMap();

        fixture.Targeting.ArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId));

        Assert.AreEqual(ItemDrop.OneUnitOf(fixture.StackInstanceId), fixture.State.ArmedDrop);
        Assert.IsTrue(fixture.State.IsAnythingArmed);
        var targetable = fixture.State.TargetableTiles!.ToList();
        Assert.HasCount(9, targetable);
        Assert.Contains(PlayerPosition, targetable);
        Assert.Contains(AdjacentTile, targetable);
    }

    [TestMethod]
    public void ConfirmingOnATileInReach_ConfirmsTheDrop_AndDisarms()
    {
        var fixture = BuildMap();
        fixture.Targeting.ArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId), HotkeySlot.Slot1);
        fixture.State.HoveredTile = AdjacentTile;

        fixture.Targeting.HandleHotkeySlotPress(HotkeySlot.Slot1);

        CollectionAssert.AreEqual(new[] { (ItemDrop.OneUnitOf(fixture.StackInstanceId), AdjacentTile) }, fixture.ConfirmedDrops);
        Assert.IsFalse(fixture.State.IsAnythingArmed);
    }

    [TestMethod]
    public void AnArmedDrop_IsRefusedOverATileThatWouldNotTakeIt_AndOffTheMap()
    {
        var fixture = BuildMap();
        fixture.Targeting.ArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId));

        fixture.State.HoveredTile = AdjacentTile;
        Assert.IsFalse(fixture.Targeting.IsArmedConfirmRefused);

        fixture.State.HoveredTile = TwoTilesAway;
        Assert.IsTrue(fixture.Targeting.IsArmedConfirmRefused);

        fixture.State.HoveredTile = null;
        Assert.IsTrue(fixture.Targeting.IsArmedConfirmRefused);
    }

    [TestMethod]
    public void AConfirmThePlacementRefuses_StaysArmed()
    {
        var fixture = BuildMap();
        fixture.Targeting.ArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId), HotkeySlot.Slot1);
        fixture.State.HoveredTile = TwoTilesAway;

        fixture.Targeting.HandleHotkeySlotPress(HotkeySlot.Slot1);

        CollectionAssert.AreEqual(new[] { (ItemDrop.OneUnitOf(fixture.StackInstanceId), TwoTilesAway) }, fixture.ConfirmedDrops, "Any tile is offered: the drop rule decides.");
        Assert.IsNotNull(fixture.State.ArmedDrop);
    }

    [TestMethod]
    public void ADropTheGameWouldRefuse_IsNeverArmed()
    {
        var fixture = BuildMap();

        fixture.Targeting.ArmDrop(ItemDrop.OneUnitOf(stackInstanceId: 12345));

        Assert.IsFalse(fixture.State.IsAnythingArmed);
    }

    [TestMethod]
    public void WhileAShopIsOpen_NothingCanBeDropped()
    {
        var fixture = BuildMap();
        fixture.State.OpenShopEntityId = 50;

        Assert.IsFalse(fixture.Targeting.CanArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId)));
        fixture.Targeting.ArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId));
        Assert.IsFalse(fixture.State.IsAnythingArmed);
    }

    [TestMethod]
    public void AnArmedDropWhoseStackIsGone_IsDisarmed()
    {
        var fixture = BuildMap();
        fixture.Targeting.ArmDrop(ItemDrop.WholeStackOf(fixture.StackInstanceId));

        fixture.Map.ComponentManager.GetMultiPool<InventoryItemStackComponent>().Remove(PlayerEntityId);
        fixture.Targeting.Tick();

        Assert.IsFalse(fixture.State.IsAnythingArmed);
    }

    [TestMethod]
    public void Cancelling_DisarmsADrop()
    {
        var fixture = BuildMap();
        fixture.Targeting.ArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId));

        fixture.Targeting.CancelArmedOrPendingAction();

        Assert.IsNull(fixture.State.ArmedDrop);
    }

    [TestMethod]
    public void AHotbarItemSlot_DropsOneUnitOfItsStack()
    {
        var fixture = BuildMap();
        fixture.Map.ComponentManager.GetMultiPool<ItemHotkeyBindingComponent>().Add(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot2, fixture.StackInstanceId));

        Assert.IsTrue(fixture.Targeting.TryGetSlotDrop(HotkeySlot.Slot2, out var drop));
        Assert.AreEqual(ItemDrop.OneUnitOf(fixture.StackInstanceId), drop);
        Assert.IsFalse(fixture.Targeting.TryGetSlotDrop(HotkeySlot.Slot3, out _));
    }

    private sealed record GridFixture(Window HostWindow, ContextMenuController ContextMenuController, TestPlayerItemDrops Drops, uint StackInstanceId);

    private static GridFixture BuildGrid(int gridEntityId, Guid? itemId = null, bool canTrade = true)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));

        var fontService = TestFonts.Shared;
        var windowService = TestElementPoolServiceFactory.Create(fontService);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver);
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(TestElementServices.For(windowService, fontService), spriteSheetService));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(windowService, fontService)));

        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var itemCatalog = new ItemCatalog();
        var definitionId = itemId ?? Guid.NewGuid();
        itemCatalog.Register(new ItemDefinition(definitionId, "Test Thing", null, "t", Color.White, Tags: [], Effects: [], CanTrade: canTrade));

        var tooltipController = new TooltipController();
        tooltipController.Initialize(windowService, new UiLayerStack());

        var drops = new TestPlayerItemDrops();
        var services = TestInventoryServices.Over(componentManager, itemCatalog, world, playerItemDrops: drops);
        var grid = new InventoryGridContent(world, services, windowService, contextMenuController, gridEntityId, filterTag: Engine.Tags.GameplayTag.None, static () => null, new MapViewState(), static (_, _) => { }, static (_, _) => { }, static _ => { });

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();
        grid.Initialize(hostWindow);

        var stackInstanceId = InventoryActions.AddItem(componentManager, gridEntityId, definitionId, quantity: 2);
        grid.Update(new GameTime());

        return new GridFixture(hostWindow, contextMenuController, drops, stackInstanceId);
    }

    private static List<Button> OpenMenu(GridFixture fixture)
    {
        fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single().OnRightClicked!.Invoke(Point.Zero);
        return fixture.ContextMenuController.Menu.ChildElements.OfType<Button>().ToList();
    }

    private static void ClickButton(Button button) => button.HandleClick(button.Rectangle.Center);

    [TestMethod]
    public void ThePlayersOwnCell_OffersDropAndDropAll_ArmingOneUnitAndTheStack()
    {
        var fixture = BuildGrid(PlayerEntityId);
        var buttons = OpenMenu(fixture);

        ClickButton(buttons.Single(button => button.LeftText == "Drop"));
        ClickButton(OpenMenu(fixture).Single(button => button.LeftText == "Drop All"));

        CollectionAssert.AreEqual(
            new[] { ItemDrop.OneUnitOf(fixture.StackInstanceId), ItemDrop.WholeStackOf(fixture.StackInstanceId) },
            fixture.Drops.ArmedDrops.Select(armed => armed.Drop).ToArray());
    }

    [TestMethod]
    public void AnotherEntitysCell_OffersNoDrop()
    {
        var fixture = BuildGrid(gridEntityId: 7);

        Assert.IsFalse(OpenMenu(fixture).Any(button => button.LeftText.StartsWith("Drop")));
    }

    [TestMethod]
    public void AnItemThatNeverLeavesItsOwner_OffersNoDrop()
    {
        var fixture = BuildGrid(PlayerEntityId, canTrade: false);

        Assert.IsFalse(OpenMenu(fixture).Any(button => button.LeftText.StartsWith("Drop")));
    }
}
