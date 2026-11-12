using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Containers.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles;
using Game.Modules.Shops.Components;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;
using Presentation.UI;
using Presentation.UI.Inventory;
using Tests.UIEngine;
using UIEngine.Input;
using UIEngine.UI.Content;

namespace Tests.Presentation;

/// <summary>A drag out of the inventory held over the map arms its drop, and the cursor says what a held drag or an armed drop would do.</summary>
[TestClass]
[DoNotParallelize]
public sealed class DragDropArmingTests
{
    private const int PlayerEntityId = TestMapWindows.PlayerEntityId;
    private const int ChestEntityId = 10;
    private static readonly Vector3Int PlayerPosition = new(20, 20, 0);
    private static readonly Vector3Int AdjacentTile = new(21, 20, 0);
    private static readonly Guid PotionItemId = Guid.NewGuid();

    /// <summary>Arms and checks drops through the real ActionTargetingController, as PlayerItemDrops does; places nothing.</summary>
    private sealed class ControllerArming(ActionTargetingController actionTargeting) : IPlayerItemDrops
    {
        public bool CanArmDrop(ItemDrop drop) => actionTargeting.CanArmDrop(drop);

        public void ArmDrop(ItemDrop drop, HotkeySlot? slot = null) => actionTargeting.ArmDrop(drop, slot);

        public bool CanDropAt(ItemDrop drop, Vector3Int tile) => true;

        public bool TryDropAt(ItemDrop drop, Vector3Int tile) => true;
    }

    private sealed record Fixture(TestMapWindows.MapWindowHarness Map, DragDropArming Handler, CursorTextContent CursorText, DropCursorText DropCursorText, uint StackInstanceId)
    {
        public MapViewState State => Map.MapViewState;

        public DragPayload FromInventory => new(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: StackInstanceId, OriginEntityId: PlayerEntityId);

        public void HoldOverMap(object payload, int frames, MapPointerHit hit)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                Handler.OnDragOver(payload, hit, Point.Zero);
            }
        }
    }

    private static Fixture Build()
    {
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [], Effects: []));
        var map = TestMapWindows.Create(60, 60, 1, PlayerPosition, itemCatalog: itemCatalog);
        var stackInstanceId = InventoryActions.AddItem(map.ComponentManager, PlayerEntityId, PotionItemId, quantity: 3);
        var cursorText = new CursorTextContent(new PointerState(), TestFonts.Shared);
        var dropCursorText = new DropCursorText(cursorText, map.ActionTargetingController);
        var handler = new DragDropArming(map.MapView, map.MapViewState, new ControllerArming(map.ActionTargetingController), map.ActionTargetingController, dropCursorText);
        return new Fixture(map, handler, cursorText, dropCursorText, stackInstanceId);
    }

    private static MapPointerHit EmptyHit(Vector3Int tile) => new(tile, []);

    private static int GraceFrames => ItemDetailsWindowController.PointerAwayGraceFrames;

    [TestMethod]
    public void AnInventoryDragHeldOverTheMapPastTheGrace_ArmsItsDrop_AndTheCursorSaysDrop()
    {
        var fixture = Build();

        fixture.HoldOverMap(fixture.FromInventory, GraceFrames, EmptyHit(AdjacentTile));
        Assert.IsNull(fixture.State.ArmedDrop, "Not yet: the drag has only just left.");

        fixture.HoldOverMap(fixture.FromInventory, 1, EmptyHit(AdjacentTile));

        Assert.AreEqual(ItemDrop.WholeStackOf(fixture.StackInstanceId), fixture.State.ArmedDrop);
        Assert.HasCount(9, fixture.State.TargetableTiles!);
        Assert.AreEqual(DropCursorText.DropText, fixture.CursorText.ShownText);
    }

    [TestMethod]
    public void ComingBackOffTheMapBeforeTheGrace_StartsTheCountAgain()
    {
        var fixture = Build();

        fixture.HoldOverMap(fixture.FromInventory, GraceFrames, EmptyHit(AdjacentTile));
        fixture.Handler.OnDragOver(fixture.FromInventory, null, Point.Zero);
        fixture.HoldOverMap(fixture.FromInventory, GraceFrames, EmptyHit(AdjacentTile));

        Assert.IsNull(fixture.State.ArmedDrop);
    }

    [TestMethod]
    public void AnArmedDropOverAContainer_SaysAdd_AndLightsIt_AndOverAShopSaysDropInRed()
    {
        var fixture = Build();
        _ = new ArmedDropHover(fixture.Map.MapView, fixture.State, new ControllerArming(fixture.Map.ActionTargetingController), fixture.DropCursorText, fixture.Map.ActionTargetingController);
        PlaceChest(fixture, AdjacentTile);
        fixture.Map.ActionTargetingController.ArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId));

        HoverOver(fixture, AdjacentTile);
        Assert.AreEqual(DropCursorText.AddText, fixture.CursorText.ShownText);
        Assert.AreEqual((ChestEntityId, true), fixture.State.ContainerDropHighlight);

        fixture.Map.ComponentManager.Merge(ChestEntityId, new ShopComponent(acceptedItems: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));
        HoverOver(fixture, AdjacentTile);
        Assert.AreEqual(DropCursorText.DropText, fixture.CursorText.ShownText);
        Assert.AreEqual((ChestEntityId, false), fixture.State.ContainerDropHighlight);

        fixture.Map.ActionTargetingController.CancelArmedOrPendingAction();
        Assert.IsNull(fixture.State.ContainerDropHighlight);
        Assert.AreEqual(string.Empty, fixture.CursorText.ShownText);
    }

    private static void PlaceChest(Fixture fixture, Vector3Int position)
    {
        var transform = new TransformComponent(position, new Vector2Byte(1, 1));
        fixture.Map.ComponentManager.Merge(ChestEntityId, transform);
        fixture.Map.World.PlaceEntityOnMap(ChestEntityId, position, ref transform);
        fixture.Map.ComponentManager.Merge(ChestEntityId, new ContainerComponent());
    }

    private static void HoverOver(Fixture fixture, Vector3Int tile)
    {
        var mapWindow = fixture.Map.MapWindow;
        for (var screenY = (int)mapWindow.ContentAbsolutePosition.Y; screenY < mapWindow.Rectangle.Bottom; screenY++)
        {
            for (var screenX = (int)mapWindow.ContentAbsolutePosition.X; screenX < mapWindow.Rectangle.Right; screenX++)
            {
                if (mapWindow.TryGetPointerHit(new Point(screenX, screenY), out var hit) && hit.Tile == tile)
                {
                    fixture.Map.ActionTargetingController.UpdateHoveredTile(new Point(screenX, screenY), mapWindow.ContentAbsolutePosition);
                    return;
                }
            }
        }

        Assert.Fail($"{tile} is not in the map window's view.");
    }

    [TestMethod]
    public void EndingTheDrag_DisarmsTheDropItArmed_AndClearsTheCursor()
    {
        var fixture = Build();
        fixture.HoldOverMap(fixture.FromInventory, GraceFrames + 1, EmptyHit(AdjacentTile));

        fixture.Handler.OnDragEnded(fixture.FromInventory);

        Assert.IsFalse(fixture.State.IsAnythingArmed);
        Assert.AreEqual(string.Empty, fixture.CursorText.ShownText);
    }

    [TestMethod]
    public void AHotbarDrag_SaysUnbindOffTheSlots_AndNeverArmsADrop()
    {
        var fixture = Build();
        var fromSlot = new DragPayload(DragPayloadOrigin.HotbarSlot, ItemStackInstanceId: fixture.StackInstanceId, OriginEntityId: PlayerEntityId, OriginSlot: HotkeySlot.Slot1);

        fixture.HoldOverMap(fromSlot, GraceFrames + 1, EmptyHit(AdjacentTile));

        Assert.IsNull(fixture.State.ArmedDrop);
        Assert.AreEqual(DropCursorText.UnbindText, fixture.CursorText.ShownText);

        fixture.Handler.OnDragEnded(fromSlot);
        Assert.AreEqual(string.Empty, fixture.CursorText.ShownText);
    }

    [TestMethod]
    public void OverAHotbarSlot_AnythingASlotCanHoldSaysBind()
    {
        var fixture = Build();
        var overSlot = new HotbarSlotDropTarget(null!, HotkeySlot.Slot2);
        var fromSlot = new DragPayload(DragPayloadOrigin.HotbarSlot, ActionId: Guid.NewGuid(), OriginSlot: HotkeySlot.Slot1);
        var mergedStack = new DragPayload(DragPayloadOrigin.InventoryCell, MergedItemDefinitionId: PotionItemId, OriginEntityId: PlayerEntityId);

        fixture.Handler.OnDragOver(fixture.FromInventory, overSlot, Point.Zero);
        Assert.AreEqual(DropCursorText.BindText, fixture.CursorText.ShownText, "An item from the inventory.");

        fixture.Handler.OnDragOver(fromSlot, overSlot, Point.Zero);
        Assert.AreEqual(DropCursorText.BindText, fixture.CursorText.ShownText, "An action from another slot.");

        fixture.Handler.OnDragOver(mergedStack, overSlot, Point.Zero);
        Assert.AreEqual(string.Empty, fixture.CursorText.ShownText, "A Merged Stack never binds.");
    }

    [TestMethod]
    public void AMenuArmedDrop_SaysDrop_UntilItIsDisarmed()
    {
        var fixture = Build();

        fixture.Map.ActionTargetingController.ArmDrop(ItemDrop.OneUnitOf(fixture.StackInstanceId));
        Assert.AreEqual(DropCursorText.DropText, fixture.CursorText.ShownText);

        fixture.Map.ActionTargetingController.CancelArmedOrPendingAction();
        Assert.AreEqual(string.Empty, fixture.CursorText.ShownText);
    }

    [TestMethod]
    public void AnotherPersistentMessage_IsNeverHiddenByTheDropText()
    {
        var fixture = Build();
        fixture.CursorText.ShowPersistent(new object(), "Select next item...");

        fixture.Handler.OnDragEnded(fixture.FromInventory);

        Assert.AreEqual("Select next item...", fixture.CursorText.ShownText);
    }
}
