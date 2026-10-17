using Game.Modules.Currency.Components;
using Game.Blueprints;
using Game.Effects;
using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Containers.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory.Components;
using Game.Modules.Shops.Components;

using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Views;

[TestClass]
public sealed class MapViewQueryTests
{
    private const int UnderGround = 0;
    private const int Ground = 1;
    private const int Flying = 2;

    private static readonly Guid ChargingActionId = new("5d0c1f7e-1f1a-4d7e-9f10-3a6c9b2e7a11");

    private sealed class Fixture
    {
        public Game.World.World World { get; }
        public ComponentManager Components { get; } = BuiltInTestComponents.RegisterAll(new ComponentManager(32, 16));
        public ActionCatalog Actions { get; } = new();
        public TerrainRegistry Terrain { get; } = new();
        public SimulationClock Clock { get; } = new();
        public MapViewQuery Query { get; }

        public Fixture()
        {
            World = TestWorlds.Over(new Map(new Vector3Int(10, 10, 3)), Components);
            Query = new MapViewQuery(World, Components, Actions, Terrain, creatures: new BlueprintRegistry(), Clock);
        }

        public void Place(int entityId, Vector3Int position)
        {
            TestTransforms.Set(Components, entityId, new TransformComponent(position, new Vector2Byte(1, 1)));
            World.PlaceEntityOnMap(entityId, position, ref Components.GetDirectPool<TransformComponent>().Get(entityId));
        }

        public void PlaceTerrain(int x, int y, TerrainLayer layer, string glyph = "~", Color glyphColor = default)
        {
            var typeId = Terrain.Register(new TerrainDefinition($"test:terrain-{Terrain.Count}", "Sand", "Loose and warm.", glyph, glyphColor));
            World.PopulateTerrain(x, y, layer, new TerrainCell(typeId));
        }

        public void PlaceStructure(Vector3Int position)
        {
            var typeId = Terrain.Register(new TerrainDefinition($"test:structure-{Terrain.Count}", "Pillar", "Holds up the ceiling.", "#", Color.Gray, BlocksMovement: true));
            World.PopulateStructure(position, new TerrainCell(typeId));
        }

    }

    // --- Terrain and structures. ------------------------------------------------------------

    [TestMethod]
    public void CopyCells_ReadsTerrainAndStructuresOfTheLayer_WithTheirSeeds()
    {
        var fixture = new Fixture();
        fixture.PlaceTerrain(2, 2, TerrainLayer.Ground);
        fixture.PlaceStructure(new Vector3Int(3, 2, Ground));
        var cells = new MapCellBuffer();

        fixture.Query.CopyCells(1, 1, 4, 3, Ground, cells);

        Assert.AreEqual(fixture.World.GetTerrainAt(new Vector3Int(2, 2, Ground)).TypeId, cells.TerrainTypeIdAt(2, 2));
        Assert.AreEqual(fixture.World.GetTerrainSeedAt(new Vector3Int(2, 2, Ground)), cells.TerrainSeedAt(2, 2));
        Assert.AreEqual(fixture.World.GetStructureAt(new Vector3Int(3, 2, Ground)).TypeId, cells.StructureTypeIdAt(3, 2));
        Assert.AreEqual(fixture.World.GetStructureSeedAt(new Vector3Int(3, 2, Ground)), cells.StructureSeedAt(3, 2));
        Assert.AreEqual((ushort)0, cells.StructureTypeIdAt(2, 2));
        Assert.IsTrue(cells.IsOnMap(4, 3));
    }

    [TestMethod]
    public void CopyCells_ALayerWithNoFloor_HasNoTerrain_ButItsOwnStructures()
    {
        var fixture = new Fixture();
        fixture.PlaceTerrain(2, 2, TerrainLayer.Ground);
        fixture.PlaceStructure(new Vector3Int(2, 2, Flying));
        var cells = new MapCellBuffer();

        fixture.Query.CopyCells(0, 0, 5, 5, Flying, cells);

        Assert.AreEqual((ushort)0, cells.TerrainTypeIdAt(2, 2));
        Assert.AreNotEqual((ushort)0, cells.StructureTypeIdAt(2, 2));
    }

    [TestMethod]
    public void CopyCells_CellsOffTheMap_AreOffTheMapAndEmpty()
    {
        var fixture = new Fixture();
        var cells = new MapCellBuffer();

        fixture.Query.CopyCells(-2, -2, 15, 15, Ground, cells);

        Assert.IsFalse(cells.IsOnMap(-1, 5));
        Assert.IsFalse(cells.IsOnMap(10, 5));
        Assert.IsTrue(cells.IsOnMap(9, 9));
        Assert.IsFalse(cells.IsOnMap(50, 50), "Outside the copied rectangle reads as off the map.");
    }

    [TestMethod]
    public void CopyCells_AcrossANeighborhoodBorderAndAnUnloadedOne_MatchesTheMapCellByCell()
    {
        var components = BuiltInTestComponents.RegisterAll(new ComponentManager(32, 16));
        var terrain = new TerrainRegistry();
        var grass = terrain.Register(new TerrainDefinition("test:grass", "Grass", "", ",", Color.Green));
        var wall = terrain.Register(new TerrainDefinition("test:wall", "Wall", "", "#", Color.Gray, BlocksMovement: true));
        var map = Map.Unbounded(depth: 3);
        map.LoadNeighborhood(map.CreateLayout(0, 0, layoutSeed: 11));
        map.LoadNeighborhood(map.CreateLayout(1, 0, layoutSeed: 12));
        var world = TestWorlds.Over(map, components, terrain: terrain);
        for (var x = 1018; x < 1030; x++)
        {
            world.PopulateTerrain(x, 4, TerrainLayer.Ground, new TerrainCell(grass));
            world.PopulateStructure(new Vector3Int(x, 5, Ground), new TerrainCell(wall));
        }

        var query = new MapViewQuery(world, components, new ActionCatalog(), terrain, new BlueprintRegistry(), new SimulationClock());
        var cells = new MapCellBuffer();
        query.CopyCells(1016, 2, 20, 6, Ground, cells);

        for (var y = 2; y < 8; y++)
        {
            for (var x = 1016; x < 1036; x++)
            {
                var position = new Vector3Int(x, y, Ground);
                Assert.AreEqual(world.IsOnMap(position), cells.IsOnMap(x, y), $"({x}, {y}) on the map");
                Assert.AreEqual(world.GetTerrainAt(position).TypeId, cells.TerrainTypeIdAt(x, y), $"({x}, {y}) terrain");
                Assert.AreEqual(world.GetTerrainSeedAt(position), cells.TerrainSeedAt(x, y), $"({x}, {y}) terrain seed");
                Assert.AreEqual(world.GetStructureAt(position).TypeId, cells.StructureTypeIdAt(x, y), $"({x}, {y}) structure");
                Assert.AreEqual(world.GetStructureSeedAt(position), cells.StructureSeedAt(x, y), $"({x}, {y}) structure seed");
            }
        }

        Assert.IsTrue(cells.IsOnMap(1030, 4));
        Assert.IsFalse(cells.IsOnMap(2048, 4), "Neighborhood (2, 0) isn't loaded.");
    }

    [TestMethod]
    public void CopyCells_ABoundedMapNarrowerThanANeighborhood_StopsAtItsEdge()
    {
        var fixture = new Fixture();
        var cells = new MapCellBuffer();

        fixture.Query.CopyCells(8, 0, 4, 1, Ground, cells);

        Assert.IsTrue(cells.IsOnMap(9, 0));
        Assert.IsFalse(cells.IsOnMap(10, 0));
    }

    [TestMethod]
    public void Terrain_IsTheRegistryTheTypeIdsReferTo()
    {
        var fixture = new Fixture();

        Assert.AreSame(fixture.Terrain, fixture.Query.Terrain);
    }

    // --- Visuals. ---------------------------------------------------------------------------

    [TestMethod]
    public void TryGetVisual_SpriteOverride_WinsOverAGlyph_ByNameAndSeed()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new SpriteComponent("Goblin", 42));
        fixture.Components.Merge(5, new GlyphComponent("g", Color.Red));

        Assert.IsTrue(fixture.Query.TryGetVisual(5, out var visual));
        Assert.AreEqual("Goblin", visual.SpriteName);
        Assert.AreEqual(42u, visual.SpriteSeed);
        Assert.AreEqual(string.Empty, visual.Glyph);
    }

    [TestMethod]
    public void TryGetVisual_GlyphOnly_FallsBackToGlyph()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new GlyphComponent("g", Color.Red));

        Assert.IsTrue(fixture.Query.TryGetVisual(5, out var visual));
        Assert.IsNull(visual.SpriteName);
        Assert.AreEqual("g", visual.Glyph);
        Assert.AreEqual(Color.Red, visual.GlyphColor);
    }

    [TestMethod]
    public void TryGetVisual_Corpse_IsDead()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new GlyphComponent("g", Color.Red));
        fixture.Components.Merge(5, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));

        Assert.IsTrue(fixture.Query.TryGetVisual(5, out var visual));
        Assert.IsTrue(visual.IsDead);
    }

    [TestMethod]
    public void TryGetVisual_NeitherSpriteNorGlyph_IsFalse() =>
        Assert.IsFalse(new Fixture().Query.TryGetVisual(5, out _));

    [TestMethod]
    public void TryGetTerrain_ReportsTheDefinitionsNameDescriptionAndLook_WithTheCellsSeed()
    {
        var fixture = new Fixture();
        fixture.PlaceTerrain(2, 2, TerrainLayer.Ground, glyph: "~", glyphColor: Color.Yellow);

        Assert.IsTrue(fixture.Query.TryGetTerrain(2, 2, Ground, out var terrain));
        Assert.AreEqual("Sand", terrain.Name);
        Assert.AreEqual("Loose and warm.", terrain.Description);
        Assert.AreEqual("~", terrain.Visual.Glyph);
        Assert.AreEqual(Color.Yellow, terrain.Visual.GlyphColor);
        Assert.AreEqual(fixture.World.GetTerrainSeedAt(new Vector3Int(2, 2, Ground)), terrain.Visual.Seed);
        Assert.IsFalse(fixture.Query.TryGetTerrain(2, 2, Flying, out _));
    }

    [TestMethod]
    public void TryGetStructure_ReadsOnlyItsOwnMapLayer_IncludingOneWithNoFloor()
    {
        var fixture = new Fixture();
        fixture.PlaceStructure(new Vector3Int(2, 2, Flying));

        Assert.IsTrue(fixture.Query.TryGetStructure(2, 2, Flying, out var structure));
        Assert.AreEqual("Pillar", structure.Name);
        Assert.AreEqual("Holds up the ceiling.", structure.Description);
        Assert.AreEqual("#", structure.Visual.Glyph);
        Assert.IsFalse(fixture.Query.TryGetStructure(2, 2, Ground, out _));
        Assert.IsFalse(fixture.Query.TryGetTerrain(2, 2, Flying, out _));
    }

    [TestMethod]
    public void TryGetTerrain_EmptyCell_IsFalse() =>
        Assert.IsFalse(new Fixture().Query.TryGetTerrain(2, 2, Ground, out _));

    // --- Occupants and status. --------------------------------------------------------------

    [TestMethod]
    public void TryGetOccupant_CombinesNonBlockingKinds()
    {
        var fixture = new Fixture();
        TestTransforms.Set(fixture.Components, 5, new TransformComponent(new Vector3Int(1, 2, Ground), new Vector2Byte(2, 2)));
        fixture.Components.GetMultiPool<NonBlockingComponent>().Add(5, new NonBlockingComponent(NonBlockingKind.Tiny));
        fixture.Components.GetMultiPool<NonBlockingComponent>().Add(5, new NonBlockingComponent(NonBlockingKind.Phasing));

        Assert.IsTrue(fixture.Query.TryGetOccupant(5, out var occupant));
        Assert.AreEqual(new Vector3Int(1, 2, Ground), occupant.Position);
        Assert.AreEqual(new Vector2Byte(2, 2), occupant.Size);
        Assert.AreEqual(NonBlockingKind.Tiny | NonBlockingKind.Phasing, occupant.Kind);
    }

    [TestMethod]
    public void GetStatus_FullHealth_HidesTheBar()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));

        Assert.IsNull(fixture.Query.GetStatus(5).HealthFraction);
    }

    [TestMethod]
    public void GetStatus_Damaged_ReportsTheFraction()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new SimpleHealthComponent(currentHealth: 25, maximumHealth: 100));

        Assert.AreEqual(0.25f, fixture.Query.GetStatus(5).HealthFraction);
    }

    /// <summary>A live creature carries inventory too, but never shows a loot bag -- only a corpse or a container does.</summary>
    [TestMethod]
    public void GetStatus_LiveCreatureCarryingItems_ShowsNoLootBag()
    {
        var fixture = new Fixture();
        fixture.Components.GetMultiPool<InventoryItemStackComponent>().Add(5, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1));

        Assert.AreEqual(LootBagState.None, fixture.Query.GetStatus(5).LootBag);
    }

    [TestMethod]
    public void GetStatus_CorpseCarryingItems_ShowsUnlootedThenLooted()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));
        fixture.Components.GetMultiPool<InventoryItemStackComponent>().Add(5, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1));
        Assert.AreEqual(LootBagState.Unlooted, fixture.Query.GetStatus(5).LootBag);

        fixture.Components.Merge(5, new LootedComponent());

        Assert.AreEqual(LootBagState.Looted, fixture.Query.GetStatus(5).LootBag);
    }

    [TestMethod]
    public void GetStatus_CorpseCarryingOnlyCurrency_ShowsALootBag()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));
        fixture.Components.Merge(5, new CurrencyComponent(gold: 0, credits: 1));

        Assert.AreEqual(LootBagState.Unlooted, fixture.Query.GetStatus(5).LootBag);
    }

    [TestMethod]
    public void GetStatus_CorpseWithAnEmptyPurse_ShowsNoLootBag()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));
        fixture.Components.Merge(5, new CurrencyComponent(gold: 0, credits: 0));

        Assert.AreEqual(LootBagState.None, fixture.Query.GetStatus(5).LootBag);
    }

    [TestMethod]
    public void GetStatus_EmptyContainer_IsAContainerWithNoLootBag()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new ContainerComponent());

        var status = fixture.Query.GetStatus(5);

        Assert.IsTrue(status.IsContainer);
        Assert.AreEqual(LootBagState.None, status.LootBag);
    }

    [TestMethod]
    public void GetStatus_StandingShopWithStock_IsNotLootableAndShowsNoLootBag()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new ShopComponent(acceptedItems: null, buyMultiplier: 1f, sellMultiplier: 1f));
        fixture.Components.Merge(5, new ContainerComponent());
        fixture.Components.GetMultiPool<InventoryItemStackComponent>().Add(5, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1));

        var status = fixture.Query.GetStatus(5);

        Assert.IsFalse(status.IsContainer);
        Assert.AreEqual(LootBagState.None, status.LootBag);
    }

    [TestMethod]
    public void GetStatus_DestroyedShopWithStock_ShowsALootBag()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new ShopComponent(acceptedItems: null, buyMultiplier: 1f, sellMultiplier: 1f));
        fixture.Components.Merge(5, new ContainerComponent());
        fixture.Components.Merge(5, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));
        fixture.Components.GetMultiPool<InventoryItemStackComponent>().Add(5, new InventoryItemStackComponent(Guid.NewGuid(), quantity: 1));

        Assert.AreEqual(LootBagState.Unlooted, fixture.Query.GetStatus(5).LootBag);
    }

    // --- Charging and interaction. ----------------------------------------------------------

    [TestMethod]
    public void TryGetChargingAction_PendingKnownAction_ReportsItsGlyph()
    {
        var fixture = new Fixture();
        fixture.Actions.Register(new ActionDefinition(
            ChargingActionId, "Slam", null, "!", Color.Orange, [],
            Effects: [Effect.None],
            Activator: new DirectAction(new TargetingSpec(TargetShape.Adjacent, Range: 0), new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 45, CooldownFrames: null))));
        fixture.Components.Merge(5, new ActionInstanceComponent(ChargingActionId, overrideDefinition: null));
        fixture.Components.Merge(5, PendingWindupComponent.ForAction(ChargingActionId, default, readyAtFrame: 45));
        fixture.Components.Merge(5, new ActionLockComponent(currentLockTotalFrames: 45, unlockedAtFrame: 45));

        Assert.IsTrue(fixture.Query.TryGetChargingAction(5, out var action));
        Assert.IsNull(action.SpriteName);
        Assert.AreEqual("!", action.Glyph);
        Assert.AreEqual(Color.Orange, action.GlyphColor);
    }

    [TestMethod]
    public void TryGetChargingAction_ShowsTheEntitysOwnOverride_NotTheCatalogAction()
    {
        var fixture = new Fixture();
        var catalogAction = new ActionDefinition(
            ChargingActionId, "Slam", null, "!", Color.Orange, [],
            Effects: [Effect.None],
            Activator: new DirectAction(new TargetingSpec(TargetShape.Adjacent, Range: 0), new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 45, CooldownFrames: null)));
        fixture.Actions.Register(catalogAction);
        fixture.Components.Merge(5, new ActionInstanceComponent(ChargingActionId, overrideDefinition: catalogAction with { Glyph = "?", GlyphColor = Color.Purple }));
        fixture.Components.Merge(5, PendingWindupComponent.ForAction(ChargingActionId, default, readyAtFrame: 45));
        fixture.Components.Merge(5, new ActionLockComponent(currentLockTotalFrames: 45, unlockedAtFrame: 45));

        Assert.IsTrue(fixture.Query.TryGetChargingAction(5, out var action));
        Assert.AreEqual(("?", Color.Purple), (action.Glyph, action.GlyphColor));
    }

    [TestMethod]
    [DataRow(100L, 0f)]
    [DataRow(115L, 0.25f)]
    [DataRow(130L, 0.5f)]
    [DataRow(160L, 1f)]
    [DataRow(175L, 1f)]
    public void GetChargeFraction_MeasuresTheWindupAgainstTheSimulationClock(long now, float expectedFraction)
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, PendingWindupComponent.ForAction(ChargingActionId, default, readyAtFrame: 160));
        fixture.Components.Merge(5, new ActionLockComponent(currentLockTotalFrames: 60, unlockedAtFrame: 160));
        fixture.Clock.Advance(now);

        Assert.AreEqual(expectedFraction, fixture.Query.GetChargeFraction(5), 0.0001f);
    }

    [TestMethod]
    public void GetChargeFraction_NextWindupQueuedOnTheResolveFrame_StartsFromZero()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new ActionLockComponent(currentLockTotalFrames: 60, unlockedAtFrame: 160));
        fixture.Components.Merge(5, PendingWindupComponent.ForAction(ChargingActionId, default, readyAtFrame: 160));
        fixture.Clock.Advance(159);
        Assert.AreEqual(59f / 60f, fixture.Query.GetChargeFraction(5), 0.0001f);

        fixture.Clock.Advance(160);
        fixture.Components.Merge(5, new ActionLockComponent(currentLockTotalFrames: 60, unlockedAtFrame: 220));
        fixture.Components.GetPackedPool<PendingWindupComponent>().Remove(5);
        fixture.Components.Merge(5, PendingWindupComponent.ForAction(ChargingActionId, default, readyAtFrame: 220));

        Assert.AreEqual(0f, fixture.Query.GetChargeFraction(5));
    }

    [TestMethod]
    public void GetChargeFraction_NotWindingUp_IsZero()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new ActionLockComponent(currentLockTotalFrames: 60, unlockedAtFrame: 160));
        fixture.Clock.Advance(130);

        Assert.AreEqual(0f, fixture.Query.GetChargeFraction(5));
    }

    [TestMethod]
    public void TryGetChargingAction_UnknownAction_IsFalse()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, PendingWindupComponent.ForAction(ChargingActionId, default, readyAtFrame: 45));

        Assert.IsFalse(fixture.Query.TryGetChargingAction(5, out _));
    }

    [TestMethod]
    public void GetInteraction_DestroyedShop_ReportsBothFlags()
    {
        var fixture = new Fixture();
        fixture.Components.Merge(5, new DisplayTextComponent("Potion Shop", ""));
        fixture.Components.Merge(5, new ShopComponent(acceptedItems: null, buyMultiplier: 1f, sellMultiplier: 1f));
        fixture.Components.Merge(5, new ContainerComponent());
        fixture.Components.Merge(5, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));

        Assert.AreEqual(new EntityInteractionView("Potion Shop", IsShop: true, IsContainer: true, IsDestroyed: true, IsLootReservedFromPlayer: false), fixture.Query.GetInteraction(5));
    }

    [TestMethod]
    public void GetInteraction_NoDisplayText_IsNamedUnknown() =>
        Assert.AreEqual("Unknown", new Fixture().Query.GetInteraction(5).Name);
}
