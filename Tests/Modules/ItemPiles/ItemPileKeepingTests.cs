using Engine.ECS.Components;
using Engine.Math;
using Engine.Tiles;
using Game.Blueprints.Objects;
using Game.Bootstrap;
using Game.Floors;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.ItemPiles.Components;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Tests.Modules.ItemPiles;

/// <summary>Item piles kept in a neighborhood's record as it unloads and put back as it loads, through the module's own event subscriptions.</summary>
[TestClass]
public sealed class ItemPileKeepingTests
{
    private static readonly Vector3Int PileTile = new(10, 10, (int)MapLayer.Ground);

    private sealed record Harness(GameBuildPassResult Build, NeighborhoodRecord Record)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public int SpawnPile(Vector3Int tile) => Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(ItemPile.Id), tile));

        public List<int> Piles => [.. Components.GetPackedPool<ItemPileComponent>().EntityIds.ToArray()];

        public void Unload()
        {
            BeginUnload();
            FinishUnload();
        }

        public void BeginUnload() => Build.EcsContext.EventBus.Publish(new NeighborhoodUnloadingEvent(0, 0, Record));

        public void FinishUnload()
        {
            foreach (var pile in Piles)
            {
                Build.EcsContext.EntityManager.DestroyEntity(pile);
            }

            Build.EcsContext.EventBus.Publish(new NeighborhoodUnloadedEvent(0, 0));
        }

        public void Load() => Build.EcsContext.EventBus.Publish(new NeighborhoodPopulatedEvent(0, 0, Record));
    }

    private static Harness CreateHarness()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), sessionSeed: 1, initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(PileTile);
        return new Harness(build, new NeighborhoodRecord(0, 0, seed: 1));
    }

    [TestMethod]
    public void AnUnloadedPile_IsKeptInTheRecord_AndPutBackOnLoad_ThenForgotten()
    {
        var harness = CreateHarness();
        var pile = harness.SpawnPile(PileTile);
        var stackId = InventoryActions.AddItem(harness.Components, pile, Guid.NewGuid(), quantity: 3);

        harness.Unload();
        Assert.HasCount(1, harness.Record.KeptItemPiles);
        Assert.IsEmpty(harness.Piles);

        harness.Load();

        var restored = harness.Piles.Single();
        Assert.AreEqual(PileTile, harness.Components.GetDirectPool<TransformComponent>().GetReadonly(restored).Position);
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(harness.Components.GetMultiPool<InventoryItemStackComponent>(), restored, stackId, out var stack));
        Assert.AreEqual(3, stack.Quantity);
        Assert.IsEmpty(harness.Record.KeptItemPiles);
    }

    [TestMethod]
    public void APileWhoseTileIsNowAWall_MovesToTheNearestOpenTile()
    {
        var harness = CreateHarness();
        var pile = harness.SpawnPile(PileTile);
        InventoryActions.AddItem(harness.Components, pile, Guid.NewGuid(), quantity: 1);
        harness.Unload();

        harness.Build.World.SetStructure(PileTile, new TerrainCell(harness.Build.World.Terrain.GetId(BuiltInTerrain.StoneWallKey)));
        harness.Load();

        var position = harness.Components.GetDirectPool<TransformComponent>().GetReadonly(harness.Piles.Single()).Position;
        Assert.IsFalse(harness.Build.World.IsCellBlocked(position));
        Assert.AreEqual(1, GridDistance.ChebyshevDistance(position, PileTile));
    }

    [TestMethod]
    public void ALitUnit_ComesBackLit_WithItsToggleOn()
    {
        var harness = CreateHarness();
        var pile = harness.SpawnPile(PileTile);
        var idolStackId = InventoryActions.AddItem(harness.Components, pile, BuiltInItemIds.ToxicIdol, quantity: 1);
        Assert.IsTrue(ToggleItemActions.TryToggle(harness.Components, harness.Build.Context.Items, pile, idolStackId, out _));
        harness.Unload();

        harness.Load();

        var restored = harness.Piles.Single();
        var restoredStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(harness.Components.GetMultiPool<InventoryItemStackComponent>(), restored, restoredStacks);
        Assert.IsTrue(restoredStacks.Single().Override?.Activator is ToggleItemActivator { IsToggledOn: true });
        Assert.AreEqual(1, harness.Components.GetMultiPool<ActiveToggleComponent>().CountForEntity(restored));
    }

    [TestMethod]
    public void APileChangedWhileTheUnloadIsUnderWay_IsKeptAsItWasWhenDestroyed()
    {
        var harness = CreateHarness();
        var pile = harness.SpawnPile(PileTile);
        var stackId = InventoryActions.AddItem(harness.Components, pile, Guid.NewGuid(), quantity: 3);

        harness.BeginUnload();
        InventoryActions.RemoveOneUnit(harness.Components, pile, stackId);
        harness.FinishUnload();

        Assert.AreEqual(2, harness.Record.KeptItemPiles.Single().Stacks.Single().Quantity);
    }

    [TestMethod]
    public void AnEmptyPile_IsNotKept()
    {
        var harness = CreateHarness();
        harness.SpawnPile(PileTile);

        harness.Unload();

        Assert.IsEmpty(harness.Record.KeptItemPiles);
    }

    [TestMethod]
    public void APileDestroyedOutsideAnUnload_IsNotKept()
    {
        var harness = CreateHarness();
        var pile = harness.SpawnPile(PileTile);
        InventoryActions.AddItem(harness.Components, pile, Guid.NewGuid(), quantity: 1);
        harness.Unload();
        harness.Load();

        harness.Build.EcsContext.EntityManager.DestroyEntity(harness.Piles.Single());

        Assert.IsEmpty(harness.Record.KeptItemPiles);
    }

    [TestMethod]
    public void UnloadingTwice_NeverDoublesAPile()
    {
        var harness = CreateHarness();
        var pile = harness.SpawnPile(PileTile);
        InventoryActions.AddItem(harness.Components, pile, Guid.NewGuid(), quantity: 2);

        harness.Unload();
        harness.Load();
        harness.Unload();
        harness.Load();

        var restored = harness.Piles.Single();
        var restoredStacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(harness.Components.GetMultiPool<InventoryItemStackComponent>(), restored, restoredStacks);
        Assert.AreEqual(2, restoredStacks.Single().Quantity);
    }
}
