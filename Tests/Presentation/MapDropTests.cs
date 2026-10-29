using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Containers.Components;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Game.Modules.Death.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>What MapWindow reports under the pointer, and dropping an item from an inventory onto the map (PLAN-unified-pointer-picking.md, Phase 5).</summary>
[TestClass]
[DoNotParallelize]
public sealed class MapDropTests
{
    private const int PlayerEntityId = TestMapWindows.PlayerEntityId;
    private static readonly Vector3Int PlayerPosition = new(100, 100, 0);
    private static readonly Vector3Int AdjacentTile = new(101, 100, 0);
    private static readonly Vector3Int DistantTile = new(105, 100, 0);
    private static readonly Guid PotionItemId = Guid.NewGuid();

    private sealed record Harness(TestMapWindows.MapWindowHarness Map, ItemCatalog ItemCatalog, MapDropHandler Handler)
    {
        public ComponentManager ComponentManager => Map.ComponentManager;
    }

    private static Harness Build()
    {
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(PotionItemId, "Test Potion", null, "p", Color.White, Tags: [Game.Tags.GameTags.ItemConsumablePotion], Effects: [], GoldValue: 10));
        var map = TestMapWindows.Create(300, 300, 1, PlayerPosition, itemCatalog: itemCatalog);
        var componentManager = map.ComponentManager;
        var shopView = new ShopView(componentManager);
        var inventoryTransfer = new InventoryTransferDropHandler(map.MapViewState, shopView, new InventoryView(componentManager, itemCatalog), new InventoryCommands(componentManager, itemCatalog, map.World), new ShopCommands(componentManager, itemCatalog, new EventBus(), map.World), new CurrencyCommands(componentManager), map.World);
        return new Harness(map, itemCatalog, new MapDropHandler(map.MapView, shopView, map.MapViewState, inventoryTransfer));
    }

    private static void Place(Harness harness, int entityId, Vector3Int position, byte size = 1, NonBlockingKind? nonBlockingKind = null)
    {
        var componentManager = harness.ComponentManager;
        var transform = new TransformComponent(position, new Vector2Byte(size, size));
        componentManager.Merge(entityId, transform);
        harness.Map.World.PlaceEntityOnMap(entityId, position, ref transform);

        if (nonBlockingKind is { } kind)
        {
            componentManager.GetMultiPool<NonBlockingComponent>().Add(entityId, new NonBlockingComponent(kind));
            harness.Map.World.ConvertToNonBlocking(entityId, ref transform);
        }
    }

    private static void PlaceChest(Harness harness, int entityId, Vector3Int position, byte size = 1)
    {
        Place(harness, entityId, position, size);
        harness.ComponentManager.Merge(entityId, new ContainerComponent());
    }

    private static void PlaceCorpse(Harness harness, int entityId, Vector3Int position)
    {
        Place(harness, entityId, position, nonBlockingKind: NonBlockingKind.None);
        harness.ComponentManager.Merge(entityId, new DeadComponent(KilledBy: Game.World.ActionSource.Admin, DiedAtFrame: 0));
    }

    private static MapPointerHit HitAt(Harness harness, Vector3Int tile)
    {
        var mapWindow = harness.Map.MapWindow;
        for (var screenY = (int)mapWindow.ContentAbsolutePosition.Y; screenY < mapWindow.Rectangle.Bottom; screenY++)
        {
            for (var screenX = (int)mapWindow.ContentAbsolutePosition.X; screenX < mapWindow.Rectangle.Right; screenX++)
            {
                if (mapWindow.TryGetPointerHit(new Point(screenX, screenY), out var hit) && hit.Tile == tile)
                {
                    return hit;
                }
            }
        }

        Assert.Fail($"{tile} is not in the map window's view.");
        return null!;
    }

    private static uint GivePlayerAPotion(Harness harness) =>
        InventoryActions.AddItem(harness.ComponentManager, PlayerEntityId, PotionItemId, quantity: 1);

    private static DragPayload PlayerPotion(uint stackInstanceId) =>
        new(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: stackInstanceId, OriginEntityId: PlayerEntityId);

    private static bool Holds(Harness harness, int entityId, uint stackInstanceId) =>
        InventoryQueries.TryFindByStackInstanceId(harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), entityId, stackInstanceId, out _);

    [TestMethod]
    public void TheHit_ListsATilesOccupants_TopmostFirst()
    {
        var harness = Build();
        var tile = new Vector3Int(110, 100, 0);
        PlaceCorpse(harness, 11, tile);
        Place(harness, 12, tile, nonBlockingKind: NonBlockingKind.Tiny);
        Place(harness, 13, tile, nonBlockingKind: NonBlockingKind.Phasing);
        Place(harness, 14, tile);

        var hit = HitAt(harness, tile);

        CollectionAssert.AreEqual(new[] { 13, 14, 12, 11 }, hit.OccupantEntityIds);
    }

    [TestMethod]
    public void ALargeFootprint_IsInTheHitOnEveryTileItCovers()
    {
        var harness = Build();
        PlaceChest(harness, 10, new Vector3Int(110, 100, 0), size: 2);

        var hit = HitAt(harness, new Vector3Int(111, 101, 0));

        CollectionAssert.AreEqual(new[] { 10 }, hit.OccupantEntityIds);
    }

    [TestMethod]
    public void ADropOnAnAdjacentChest_MovesTheWholeStackIntoIt()
    {
        var harness = Build();
        PlaceChest(harness, 10, AdjacentTile);
        var stackInstanceId = GivePlayerAPotion(harness);
        var hit = HitAt(harness, AdjacentTile);

        Assert.IsFalse(harness.Handler.IsDropRefusedAt(PlayerPotion(stackInstanceId), hit, Point.Zero));
        Assert.IsTrue(harness.Handler.TryDrop(PlayerPotion(stackInstanceId), hit, Point.Zero));

        Assert.IsTrue(Holds(harness, 10, stackInstanceId));
        Assert.IsFalse(Holds(harness, PlayerEntityId, stackInstanceId));
    }

    [TestMethod]
    public void ADropOnAnAdjacentCorpse_MovesTheStackIntoIt()
    {
        var harness = Build();
        PlaceCorpse(harness, 11, AdjacentTile);
        var stackInstanceId = GivePlayerAPotion(harness);

        harness.Handler.TryDrop(PlayerPotion(stackInstanceId), HitAt(harness, AdjacentTile), Point.Zero);

        Assert.IsTrue(Holds(harness, 11, stackInstanceId));
    }

    [TestMethod]
    public void ADropOnADistantChest_IsRefused_AndMovesNothing()
    {
        var harness = Build();
        PlaceChest(harness, 10, DistantTile);
        var stackInstanceId = GivePlayerAPotion(harness);
        var hit = HitAt(harness, DistantTile);

        Assert.IsTrue(harness.Handler.IsDropRefusedAt(PlayerPotion(stackInstanceId), hit, Point.Zero));
        Assert.IsTrue(harness.Handler.TryDrop(PlayerPotion(stackInstanceId), hit, Point.Zero), "The map claims the drop, so nothing behind it acts on it.");

        Assert.IsTrue(Holds(harness, PlayerEntityId, stackInstanceId));
    }

    [TestMethod]
    public void ADropOnAShopkeeper_IsRefused()
    {
        var harness = Build();
        Place(harness, 10, AdjacentTile);
        harness.ComponentManager.Merge(10, new ContainerComponent());
        harness.ComponentManager.Merge(10, new ShopComponent(acceptedItems: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));
        var stackInstanceId = GivePlayerAPotion(harness);
        var hit = HitAt(harness, AdjacentTile);

        Assert.IsTrue(harness.Handler.IsDropRefusedAt(PlayerPotion(stackInstanceId), hit, Point.Zero));
        harness.Handler.TryDrop(PlayerPotion(stackInstanceId), hit, Point.Zero);

        Assert.IsTrue(Holds(harness, PlayerEntityId, stackInstanceId));
    }

    [TestMethod]
    public void ADropOnAnEmptyTile_IsRefused()
    {
        var harness = Build();
        var stackInstanceId = GivePlayerAPotion(harness);

        Assert.IsTrue(harness.Handler.IsDropRefusedAt(PlayerPotion(stackInstanceId), HitAt(harness, AdjacentTile), Point.Zero));
    }

    [TestMethod]
    public void AnItemFromAShop_IsRefusedOnAChest()
    {
        var harness = Build();
        PlaceChest(harness, 10, AdjacentTile);
        harness.ComponentManager.Merge(20, new ShopComponent(acceptedItems: null, buyMultiplier: 1.2f, sellMultiplier: 0.8f));
        var stackInstanceId = InventoryActions.AddItem(harness.ComponentManager, 20, PotionItemId, quantity: 1);
        var shopPotion = new DragPayload(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: stackInstanceId, OriginEntityId: 20);

        Assert.IsTrue(harness.Handler.IsDropRefusedAt(shopPotion, HitAt(harness, AdjacentTile), Point.Zero));
    }

    private static DragPayload FromHotbarSlot(uint stackInstanceId) =>
        new(DragPayloadOrigin.HotbarSlot, ItemStackInstanceId: stackInstanceId, OriginEntityId: PlayerEntityId, OriginSlot: HotkeySlot.Slot1);

    private static void FillWithStacks(Harness harness, int entityId)
    {
        for (var stackIndex = 0; stackIndex < InventoryCapacity.MaxNonPlayerStackCount; stackIndex++)
        {
            harness.ComponentManager.GetMultiPool<InventoryItemStackComponent>().Add(entityId, new InventoryItemStackComponent(PotionItemId, 1));
        }
    }

    [TestMethod]
    public void AnItemFromAHotbarSlot_GoesIntoAnAdjacentChest()
    {
        var harness = Build();
        PlaceChest(harness, 10, AdjacentTile);
        var stackInstanceId = GivePlayerAPotion(harness);

        Assert.IsTrue(harness.Handler.TryDrop(FromHotbarSlot(stackInstanceId), HitAt(harness, AdjacentTile), Point.Zero));

        Assert.IsTrue(Holds(harness, 10, stackInstanceId));
    }

    [TestMethod]
    public void AnItemFromAHotbarSlot_OverATileWithNoContainer_IsLeftToTheHotbar()
    {
        var harness = Build();
        var stackInstanceId = GivePlayerAPotion(harness);
        var hit = HitAt(harness, AdjacentTile);

        Assert.IsFalse(harness.Handler.IsDropRefusedAt(FromHotbarSlot(stackInstanceId), hit, Point.Zero));
        Assert.IsFalse(harness.Handler.TryDrop(FromHotbarSlot(stackInstanceId), hit, Point.Zero));
        Assert.IsTrue(Holds(harness, PlayerEntityId, stackInstanceId));
    }

    [TestMethod]
    public void AFullChest_RefusesTheDrop_AndKeepsTheItemWithThePlayer()
    {
        var harness = Build();
        PlaceChest(harness, 10, AdjacentTile);
        FillWithStacks(harness, 10);
        var stackInstanceId = GivePlayerAPotion(harness);
        var hit = HitAt(harness, AdjacentTile);

        Assert.IsTrue(harness.Handler.IsDropRefusedAt(PlayerPotion(stackInstanceId), hit, Point.Zero));
        harness.Handler.TryDrop(PlayerPotion(stackInstanceId), hit, Point.Zero);

        Assert.IsTrue(Holds(harness, PlayerEntityId, stackInstanceId));
    }

    [TestMethod]
    public void HoldingAnItemOverAChest_LightsItGreenWithRoom_AndRedWhenFull()
    {
        var harness = Build();
        PlaceChest(harness, 10, AdjacentTile);
        var stackInstanceId = GivePlayerAPotion(harness);
        var hit = HitAt(harness, AdjacentTile);

        harness.Handler.OnDragOver(PlayerPotion(stackInstanceId), hit, Point.Zero);
        Assert.AreEqual((10, true), harness.Map.MapViewState.ContainerDropHighlight);

        FillWithStacks(harness, 10);
        harness.Handler.OnDragOver(PlayerPotion(stackInstanceId), hit, Point.Zero);
        Assert.AreEqual((10, false), harness.Map.MapViewState.ContainerDropHighlight);
    }

    [TestMethod]
    public void HoldingAnItemOverAChestOutOfReach_LightsItRed()
    {
        var harness = Build();
        PlaceChest(harness, 10, DistantTile);
        var stackInstanceId = GivePlayerAPotion(harness);

        harness.Handler.OnDragOver(PlayerPotion(stackInstanceId), HitAt(harness, DistantTile), Point.Zero);

        Assert.AreEqual((10, false), harness.Map.MapViewState.ContainerDropHighlight);
    }

    [TestMethod]
    public void TheHighlight_ClearsOffAContainer_AndWhenTheDragEnds()
    {
        var harness = Build();
        PlaceChest(harness, 10, AdjacentTile);
        var payload = PlayerPotion(GivePlayerAPotion(harness));
        harness.Handler.OnDragOver(payload, HitAt(harness, AdjacentTile), Point.Zero);

        harness.Handler.OnDragOver(payload, HitAt(harness, DistantTile), Point.Zero);
        Assert.IsNull(harness.Map.MapViewState.ContainerDropHighlight);

        harness.Handler.OnDragOver(payload, HitAt(harness, AdjacentTile), Point.Zero);
        harness.Handler.OnDragEnded(payload);
        Assert.IsNull(harness.Map.MapViewState.ContainerDropHighlight);
    }

    [TestMethod]
    public void AnActionDraggedFromAHotbarSlot_IsLeftToTheHotbar()
    {
        var harness = Build();
        var fromSlot = new DragPayload(DragPayloadOrigin.HotbarSlot, ActionId: Guid.NewGuid(), OriginSlot: HotkeySlot.Slot1);
        var hit = HitAt(harness, AdjacentTile);

        Assert.IsFalse(harness.Handler.IsDropRefusedAt(fromSlot, hit, Point.Zero));
        Assert.IsFalse(harness.Handler.TryDrop(fromSlot, hit, Point.Zero));
    }
}
