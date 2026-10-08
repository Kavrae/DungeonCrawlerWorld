using Engine.Events;
using Engine.Math;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Auras;

[TestClass]
public sealed class AuraFieldTests
{
    private const byte OrangeAuraId = 0;
    private const byte RedAuraId = 1;
    private const byte GreenAuraId = 2;

    private static readonly Guid OrangeAuraGuid = new("00000000-0000-0000-0000-0000000000a1");
    private static readonly AuraDefinition OrangeAura = new(OrangeAuraGuid, "Orange", Color.Orange);
    private static readonly Vector3Int MapSize = new(100, 100, 1);
    private static readonly Vector3Int SourcePosition = new(10, 10, 0);

    private static readonly TerrainDefinition GlowingTerrain = new(
        "test:glowing", "Glowing", "", default, "~", default,
        Aura: new TerrainAura(OrangeAura, 8, 3));

    private static AuraCatalog BuildAuras()
    {
        var auras = new AuraCatalog();
        auras.Register(OrangeAura);
        auras.Register(new AuraDefinition(new Guid("00000000-0000-0000-0000-0000000000a3"), "Red", Color.Red));
        auras.Register(new AuraDefinition(new Guid("00000000-0000-0000-0000-0000000000a4"), "Green", Color.Green));
        return auras;
    }

    private static AuraField BuildField(Game.World.World? world = null, TerrainRegistry? terrain = null, EventBus? eventBus = null) =>
        new(world ?? TestWorlds.Create(new Map(MapSize)), terrain ?? new TerrainRegistry(), BuildAuras(), eventBus ?? new EventBus());

    [TestMethod]
    public void AddSource_GlowsItsAurasColourAtItsStrengthAtTheSource()
    {
        var field = BuildField();

        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3));

        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var glowColor, out var totalPower));
        Assert.AreEqual(Color.Orange, glowColor);
        Assert.AreEqual(8, totalPower);
        Assert.AreEqual(8, field.GetTotalPowerAt(SourcePosition, OrangeAuraId));
    }

    [TestMethod]
    public void TryGetGlow_OutsideEverySourcesReach_ReturnsFalse()
    {
        var field = BuildField();
        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3));

        Assert.IsFalse(field.TryGetGlow(new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z), out _, out _));
    }

    [TestMethod]
    public void RemoveSource_LeavesNoGlowBehind()
    {
        var field = BuildField();
        var source = new AuraSourceComponent(OrangeAuraId, power: 8, size: 3);
        field.AddSource(SourcePosition, source);

        field.RemoveSource(SourcePosition, source);

        Assert.IsFalse(field.TryGetGlow(SourcePosition, out _, out _));
        Assert.AreEqual(0, field.GetTotalPowerAt(SourcePosition, OrangeAuraId));
    }

    [TestMethod]
    public void TwoAurasOverlapping_GlowIsTheirColoursWeightedByPower()
    {
        var field = BuildField();
        field.AddSource(SourcePosition, new AuraSourceComponent(RedAuraId, power: 8, size: 3));
        field.AddSource(new Vector3Int(SourcePosition.X + 2, SourcePosition.Y, SourcePosition.Z), new AuraSourceComponent(GreenAuraId, power: 4, size: 2));

        // At the first source: power 8 of Red (distance 0), 2 of Green (distance 2 of size 2: 4 * 1 / 3, rounded up).
        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var glowColor, out var totalPower));
        Assert.AreEqual(new Color((byte)(255 * 8 / 10f), (byte)(128 * 2 / 10f), (byte)0), glowColor);
        Assert.AreEqual(10, totalPower);
    }

    /// <summary>One source radiating two auras: each keeps its own reach and colour, and taking one away leaves the other exactly as it was.</summary>
    [TestMethod]
    public void TwoAurasAtOnePosition_BlendAndAreRemovedIndependently()
    {
        var field = BuildField();
        var redSource = new AuraSourceComponent(RedAuraId, power: 8, size: 3);
        var greenSource = new AuraSourceComponent(GreenAuraId, power: 2, size: 1);
        field.AddSource(SourcePosition, redSource);
        field.AddSource(SourcePosition, greenSource);

        var threeAway = new Vector3Int(SourcePosition.X + 3, SourcePosition.Y, SourcePosition.Z);
        Assert.IsTrue(field.TryGetGlow(threeAway, out var edgeColor, out var edgePower));
        Assert.AreEqual(Color.Red, edgeColor, "Only the stronger aura reaches this far.");
        Assert.AreEqual(2, edgePower, "Power 8 at the edge of size 3: 8 * 1 / 4.");

        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var blendedColor, out var blendedPower));
        Assert.AreEqual(new Color((byte)(255 * 8 / 10f), (byte)(128 * 2 / 10f), (byte)0), blendedColor);
        Assert.AreEqual(10, blendedPower);

        field.RemoveSource(SourcePosition, redSource);

        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var remainingColor, out var remainingPower));
        Assert.AreEqual(Color.Green, remainingColor);
        Assert.AreEqual(2, remainingPower);
        Assert.IsFalse(field.TryGetGlow(threeAway, out _, out _));
    }

    /// <summary>The "any aura here at all" answer covers exactly the cells some aura reaches, and follows each aura leaving a cell on its own.</summary>
    [TestMethod]
    public void AnyAuraReaches_IsTrueWhileAtLeastOneAuraReachesTheCell()
    {
        var field = BuildField();
        var redSource = new AuraSourceComponent(RedAuraId, power: 8, size: 3);
        var greenSource = new AuraSourceComponent(GreenAuraId, power: 2, size: 1);
        var threeAway = new Vector3Int(SourcePosition.X + 3, SourcePosition.Y, SourcePosition.Z);
        var fourAway = new Vector3Int(SourcePosition.X + 4, SourcePosition.Y, SourcePosition.Z);
        Assert.IsFalse(field.AnyAuraReaches(SourcePosition));

        field.AddSource(SourcePosition, redSource);
        field.AddSource(SourcePosition, greenSource);
        Assert.IsTrue(field.AnyAuraReaches(SourcePosition));
        Assert.IsTrue(field.AnyAuraReaches(threeAway), "The stronger aura's edge.");
        Assert.IsFalse(field.AnyAuraReaches(fourAway));

        field.RemoveSource(SourcePosition, redSource);
        Assert.IsTrue(field.AnyAuraReaches(SourcePosition), "The weaker aura still reaches its own cell.");
        Assert.IsFalse(field.AnyAuraReaches(threeAway));

        field.RemoveSource(SourcePosition, greenSource);
        Assert.IsFalse(field.AnyAuraReaches(SourcePosition));
    }

    /// <summary>Two sources of one aura overlapping: a cell stays covered until the last of them is gone.</summary>
    [TestMethod]
    public void AnyAuraReaches_OverlappingSourcesOfOneAura_ClearsOnlyWhenTheLastLeaves()
    {
        var field = BuildField();
        var source = new AuraSourceComponent(OrangeAuraId, power: 8, size: 3);
        var beside = new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, SourcePosition.Z);
        field.AddSource(SourcePosition, source);
        field.AddSource(beside, source);

        field.RemoveSource(SourcePosition, source);
        Assert.IsTrue(field.AnyAuraReaches(SourcePosition));

        field.RemoveSource(beside, source);
        Assert.IsFalse(field.AnyAuraReaches(SourcePosition));
    }

    [TestMethod]
    public void AddAndRemove_EachChangeTheVersion()
    {
        var field = BuildField();
        var source = new AuraSourceComponent(OrangeAuraId, power: 8, size: 3);
        var initialVersion = field.Version;

        field.AddSource(SourcePosition, source);
        var versionAfterAdd = field.Version;
        field.RemoveSource(SourcePosition, source);

        Assert.AreNotEqual(initialVersion, versionAfterAdd);
        Assert.AreNotEqual(versionAfterAdd, field.Version);
    }

    /// <summary>Terrain cells aren't entities, so the field scans them when it is built -- lava laid down at population must glow from the first frame.</summary>
    [TestMethod]
    public void EnsureBuilt_GlowingTerrainAlreadyPlaced_IsInTheField()
    {
        var terrain = new TerrainRegistry();
        var world = TestWorlds.Create(new Map(MapSize));
        world.PopulateTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(terrain.Register(GlowingTerrain), 0));
        var field = BuildField(world, terrain);

        field.EnsureBuilt();

        Assert.AreEqual(8, field.GetTotalPowerAt(SourcePosition, OrangeAuraId));
    }

    /// <summary>The glow view needs no separate build: asking for a glow is enough.</summary>
    [TestMethod]
    public void GlowView_BeforeAnythingBuiltTheField_SeesTerrainAuras()
    {
        var terrain = new TerrainRegistry();
        var world = TestWorlds.Create(new Map(MapSize));
        world.PopulateTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(terrain.Register(GlowingTerrain), 0));
        var glowView = new AuraGlowView(BuildField(world, terrain));

        Assert.IsTrue(glowView.TryGetGlow(SourcePosition.X, SourcePosition.Y, SourcePosition.Z, out var glowColor, out _));
        Assert.AreEqual(Color.Orange, glowColor);
    }

    /// <summary>A runtime terrain change swaps the cell's aura and says where, so exposures around it can follow.</summary>
    [TestMethod]
    public void TerrainChanged_SwapsTheCellsAuraAndAnnouncesIt()
    {
        var eventBus = new EventBus();
        var terrain = new TerrainRegistry();
        var glowing = terrain.Register(GlowingTerrain);
        var plain = terrain.Register(new TerrainDefinition("test:plain", "Plain", "", default, ".", default));
        var world = TestWorlds.Create(new Map(MapSize), eventBus: eventBus);
        world.PopulateTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(glowing, 0));
        var field = BuildField(world, terrain, eventBus);
        field.EnsureBuilt();
        var announced = new List<string>();
        field.TerrainAuraAdded += (position, source) => announced.Add($"added {position.X},{position.Y} power {source.Power} size {source.Size}");
        field.TerrainAuraRemoved += (position, source) => announced.Add($"removed {position.X},{position.Y} power {source.Power} size {source.Size}");

        world.SetTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(plain, 0));
        Assert.IsFalse(field.TryGetGlow(SourcePosition, out _, out _));

        world.SetTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(glowing, 0));
        Assert.IsTrue(field.TryGetGlow(SourcePosition, out _, out _));

        CollectionAssert.AreEqual(new[] { "removed 10,10 power 8 size 3", "added 10,10 power 8 size 3" }, announced);
    }

    /// <summary>Lava just inside neighborhood 1 reaches into neighborhood 0; unloading neighborhood 1 takes that away, and loading it again restores exactly what a fresh scan sees.</summary>
    [TestMethod]
    public void TerrainUnloadingThenLoaded_RemovesAndRestoresItsReach()
    {
        var eventBus = new EventBus();
        var terrain = new TerrainRegistry();
        var glowing = new TerrainCell(terrain.Register(GlowingTerrain), 0);
        var world = TestWorlds.Create(new Map(new MapBounds(0, 0, 2048, 10, 1)));
        world.PopulateTerrain(1024, 5, TerrainLayer.UnderGround, glowing);
        var auras = BuildAuras();
        var field = new AuraField(world, terrain, auras, eventBus);
        var acrossTheBorder = new Vector3Int(1022, 5, 0);
        Assert.IsTrue(field.TryGetGlow(acrossTheBorder, out var colorBefore, out var strengthBefore), "Precondition: the aura reaches across the border.");

        eventBus.Publish(new TerrainUnloadingEvent(Neighborhoods.AreaOf(1, 0, 1)));
        world.Map.UnloadNeighborhood(1, 0);

        Assert.IsFalse(field.TryGetGlow(acrossTheBorder, out _, out _));

        world.Map.LoadNeighborhood(1, 0);
        world.PopulateTerrain(1024, 5, TerrainLayer.UnderGround, glowing);
        Assert.IsTrue(TerrainAuraSources.TryGetAura(terrain, auras, glowing.TypeId, out var aura));
        eventBus.Publish(new TerrainLoadedEvent(Neighborhoods.AreaOf(1, 0, 1), [new TerrainAuraCell(new Vector3Int(1024, 5, (int)MapLayer.UnderGround), aura)]));

        Assert.IsTrue(field.TryGetGlow(acrossTheBorder, out var colorAfter, out var strengthAfter));
        Assert.AreEqual(colorBefore, colorAfter);
        Assert.AreEqual(strengthBefore, strengthAfter);
    }

    /// <summary>On an unbounded map, lava at the edge of the only loaded neighborhood already reaches into the neighbor when that neighbor loads.</summary>
    [TestMethod]
    public void UnboundedMap_NeighborLoadedLater_IsAlreadyReachedFromAcrossTheSeam()
    {
        var (world, field, _, _, _) = BuildUnboundedWithGlowingTerrainAtTheEasternEdge();
        var acrossTheSeam = new Vector3Int(1025, 5, 0);

        world.Map.LoadNeighborhood(1, 0);

        Assert.IsTrue(field.TryGetGlow(acrossTheSeam, out _, out var totalPower));
        Assert.AreEqual(4, totalPower);
    }

    /// <summary>Unloading the neighborhood that holds the lava takes its whole reach out of the neighbor loaded after it, and loading it again puts back exactly that.</summary>
    [TestMethod]
    public void UnboundedMap_SourceNeighborhoodUnloadedAndReloaded_LeavesNothingBehindAndRestoresItsReach()
    {
        var (world, field, eventBus, glowing, aura) = BuildUnboundedWithGlowingTerrainAtTheEasternEdge();
        var acrossTheSeam = new Vector3Int(1025, 5, 0);
        world.Map.LoadNeighborhood(1, 0);

        eventBus.Publish(new TerrainUnloadingEvent(Neighborhoods.AreaOf(0, 0, 1)));
        world.Map.UnloadNeighborhood(0, 0);

        Assert.AreEqual(0, field.GetTotalPowerAt(acrossTheSeam, OrangeAuraId));
        Assert.IsFalse(field.AnyAuraReaches(acrossTheSeam));
        Assert.AreEqual(0, field.TotalsChunkCount, "Every chunk the lava's reach used, in both neighborhoods, is freed.");

        world.Map.LoadNeighborhood(0, 0);
        world.PopulateTerrain(1023, 5, TerrainLayer.UnderGround, glowing);
        eventBus.Publish(new TerrainLoadedEvent(Neighborhoods.AreaOf(0, 0, 1), [new TerrainAuraCell(new Vector3Int(1023, 5, (int)MapLayer.UnderGround), aura)]));

        Assert.AreEqual(4, field.GetTotalPowerAt(acrossTheSeam, OrangeAuraId));
    }

    /// <summary>An entity source at the window's edge is taken out whole even though the rectangle around the loaded neighborhoods changed between adding and removing it.</summary>
    [TestMethod]
    public void UnboundedMap_SourceAddedBeforeTheNeighborLoadsAndRemovedAfter_LeavesNothingBehind()
    {
        var world = TestWorlds.Create(Map.Unbounded(depth: 1));
        world.Map.LoadNeighborhood(0, 0);
        var field = BuildField(world);
        var source = new AuraSourceComponent(OrangeAuraId, power: 8, size: 3);
        var sourcePosition = new Vector3Int(1023, 5, 0);
        var acrossTheSeam = new Vector3Int(1025, 5, 0);

        field.AddSource(sourcePosition, source);
        world.Map.LoadNeighborhood(1, 0);
        field.RemoveSource(sourcePosition, source);

        Assert.AreEqual(0, field.GetTotalPowerAt(acrossTheSeam, OrangeAuraId));
        Assert.IsFalse(field.AnyAuraReaches(acrossTheSeam));
        Assert.IsFalse(field.AnyAuraReaches(sourcePosition));
    }

    private static (Game.World.World World, AuraField Field, EventBus EventBus, TerrainCell Glowing, AuraSourceComponent Aura) BuildUnboundedWithGlowingTerrainAtTheEasternEdge()
    {
        var eventBus = new EventBus();
        var terrain = new TerrainRegistry();
        var glowing = new TerrainCell(terrain.Register(GlowingTerrain), 0);
        var world = TestWorlds.Create(Map.Unbounded(depth: 1), eventBus: eventBus);
        world.Map.LoadNeighborhood(0, 0);
        world.PopulateTerrain(1023, 5, TerrainLayer.UnderGround, glowing);
        var auras = BuildAuras();
        var field = new AuraField(world, terrain, auras, eventBus);
        field.EnsureBuilt();
        Assert.IsTrue(TerrainAuraSources.TryGetAura(terrain, auras, glowing.TypeId, out var aura));

        return (world, field, eventBus, glowing, aura);
    }

    /// <summary>Replacing a terrain's definition with one that radiates a different power and size, or nothing, swaps the reach of every loaded cell of that terrain.</summary>
    [TestMethod]
    public void TerrainDefinitionReplaced_ItsCellsRadiateWhatTheNewDefinitionSays()
    {
        var eventBus = new EventBus();
        var terrain = new TerrainRegistry();
        var glowing = terrain.Register(GlowingTerrain);
        var world = TestWorlds.Create(new Map(MapSize), eventBus: eventBus);
        world.PopulateTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(glowing, 0));
        var field = BuildField(world, terrain, eventBus);
        field.EnsureBuilt();
        var announced = new List<string>();
        field.TerrainAuraAdded += (position, source) => announced.Add($"added {position.X},{position.Y} power {source.Power} size {source.Size}");
        field.TerrainAuraRemoved += (position, source) => announced.Add($"removed {position.X},{position.Y} power {source.Power} size {source.Size}");

        terrain.Register(GlowingTerrain with { Aura = new TerrainAura(OrangeAura, 2, 1) });

        Assert.AreEqual(2, field.GetTotalPowerAt(SourcePosition, OrangeAuraId));
        CollectionAssert.AreEqual(new[] { "removed 10,10 power 8 size 3", "added 10,10 power 2 size 1" }, announced);

        terrain.Register(GlowingTerrain with { Aura = null });

        Assert.IsFalse(field.AnyAuraReaches(SourcePosition));
    }

    /// <summary>The glow's colour is read from the aura's definition when it is asked for, so a replaced definition recolours what is already in the field and says the glow changed.</summary>
    [TestMethod]
    public void AuraDefinitionReplaced_TheGlowTakesItsNewColourAndTheVersionMoves()
    {
        var auras = BuildAuras();
        var field = new AuraField(TestWorlds.Create(new Map(MapSize)), new TerrainRegistry(), auras, new EventBus());
        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3));
        var versionBefore = field.Version;

        auras.Register(new AuraDefinition(OrangeAuraGuid, "Orange", Color.Purple));

        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var glowColor, out _));
        Assert.AreEqual(Color.Purple, glowColor);
        Assert.AreNotEqual(versionBefore, field.Version);
    }

    private static readonly AuraDefinition SteadyAura = new(new Guid("00000000-0000-0000-0000-0000000000a9"), "Steady", Color.Blue, Falloff: AuraFalloff.None);

    /// <summary>Every cell of a fresh field holding one source at each of positions, against what field holds now, around center.</summary>
    private static void AssertMatchesFreshField(AuraField field, byte auraId, AuraSourceComponent source, Vector3Int center, int radius, params Vector3Int[] positions)
    {
        var auras = new AuraCatalog();
        auras.Register(SteadyAura);
        auras.Register(OrangeAura);
        var fresh = new AuraField(TestWorlds.Create(new Map(MapSize)), new TerrainRegistry(), auras, new EventBus());
        foreach (var position in positions)
        {
            fresh.AddSource(position, source);
        }

        for (var y = center.Y - radius; y <= center.Y + radius; y++)
        {
            for (var x = center.X - radius; x <= center.X + radius; x++)
            {
                var cell = new Vector3Int(x, y, center.Z);
                Assert.AreEqual(fresh.GetTotalPowerAt(cell, auraId), field.GetTotalPowerAt(cell, auraId), $"Cell {cell}");
                Assert.AreEqual(fresh.AnyAuraReaches(cell), field.AnyAuraReaches(cell), $"Coverage at {cell}");
            }
        }
    }

    /// <summary>A None aura is full power to its edge, and a move of one writes only the cells that changed sides -- leaving exactly what a fresh placement at the end would.</summary>
    [TestMethod]
    public void NoneFalloff_MovedSeveralTimes_MatchesASourcePlacedFreshWhereItEnded()
    {
        var auras = new AuraCatalog();
        var steadyAuraId = auras.Register(SteadyAura);
        var field = new AuraField(TestWorlds.Create(new Map(MapSize)), new TerrainRegistry(), auras, new EventBus());
        var source = new AuraSourceComponent(steadyAuraId, power: 5, size: 6);
        var standing = new Vector3Int(30, 30, 0);
        var other = new AuraSourceComponent(steadyAuraId, power: 2, size: 3);
        var otherPosition = new Vector3Int(33, 30, 0);
        field.AddSource(standing, source);
        field.AddSource(otherPosition, other);

        Assert.AreEqual(7, field.GetTotalPowerAt(new Vector3Int(32, 30, 0), steadyAuraId), "Both reach it at full power.");
        Assert.AreEqual(5, field.GetTotalPowerAt(new Vector3Int(30, 36, 0), steadyAuraId), "The edge of size 6 is still power 5.");

        foreach (var next in new[] { new Vector3Int(31, 30, 0), new Vector3Int(31, 31, 0), new Vector3Int(40, 35, 0), new Vector3Int(39, 35, 0) })
        {
            field.MoveSource(standing, next, source);
            standing = next;
        }

        field.RemoveSource(otherPosition, other);
        AssertMatchesFreshField(field, steadyAuraId, source, standing, radius: 20, standing);
    }

    [TestMethod]
    public void LinearFalloff_Moved_MatchesASourcePlacedFreshWhereItEnded()
    {
        var auras = new AuraCatalog();
        auras.Register(SteadyAura);
        var orangeAuraId = auras.Register(OrangeAura);
        var field = new AuraField(TestWorlds.Create(new Map(MapSize)), new TerrainRegistry(), auras, new EventBus());
        var source = new AuraSourceComponent(orangeAuraId, power: 16, size: 4);
        field.AddSource(new Vector3Int(30, 30, 0), source);

        field.MoveSource(new Vector3Int(30, 30, 0), new Vector3Int(32, 31, 0), source);

        AssertMatchesFreshField(field, orangeAuraId, source, new Vector3Int(32, 31, 0), radius: 10, new Vector3Int(32, 31, 0));
    }

    /// <summary>An aura replaced with a different falloff while its sources are in the field: they keep the falloff they went in with, so taking them out leaves nothing behind, and the next source after they have gone takes the new one.</summary>
    [TestMethod]
    public void FalloffReplacedWhileSourcesAreIn_TakesEffectOnceTheyHaveGone()
    {
        var auras = BuildAuras();
        var field = new AuraField(TestWorlds.Create(new Map(MapSize)), new TerrainRegistry(), auras, new EventBus());
        var source = new AuraSourceComponent(OrangeAuraId, power: 8, size: 3);
        var threeAway = new Vector3Int(SourcePosition.X + 3, SourcePosition.Y, SourcePosition.Z);
        field.AddSource(SourcePosition, source);

        auras.Register(OrangeAura with { Falloff = AuraFalloff.None });
        field.AddSource(SourcePosition, source);

        Assert.AreEqual(4, field.GetTotalPowerAt(threeAway, OrangeAuraId), "Both written Linear: 2 each at the edge.");

        field.RemoveSource(SourcePosition, source);
        field.RemoveSource(SourcePosition, source);
        Assert.AreEqual(0, field.TotalsChunkCount, "Each taken out with the falloff it went in with.");

        field.AddSource(SourcePosition, source);
        Assert.AreEqual(8, field.GetTotalPowerAt(threeAway, OrangeAuraId), "The first source after they had gone takes the new falloff.");
    }

    private static ActionSource EntitySource(int entityId)
    {
        var components = BuiltInTestComponents.RegisterAll(new Engine.ECS.Components.ComponentManager(16, 16));
        var keys = new Engine.ECS.Entities.EntityKeys();
        for (var id = 0; id <= entityId; id++)
        {
            keys.Issue(id);
        }

        return ActionSource.FromEntity(components, keys, entityId, new Game.Blueprints.BlueprintRegistry());
    }

    private static (AuraField Field, ushort GlowingTypeId, TerrainRegistry Terrain, Game.World.World World) BuildFieldWithGlowingTerrainAt(Vector3Int position)
    {
        var terrain = new TerrainRegistry();
        var glowing = terrain.Register(GlowingTerrain);
        var world = TestWorlds.Create(new Map(MapSize));
        world.PopulateTerrain(position.X, position.Y, TerrainLayer.UnderGround, new TerrainCell(glowing, 0));
        var field = new AuraField(world, terrain, BuildAuras(), new EventBus());
        field.EnsureBuilt();
        return (field, glowing, terrain, world);
    }

    [TestMethod]
    public void Attribute_EntitySourceStrongerThanTheTerrainShare_CreditsTheEntity()
    {
        var (field, _, _, _) = BuildFieldWithGlowingTerrainAt(new Vector3Int(20, 10, 0));
        var shrine = EntitySource(5);
        field.AddEntitySource(5, SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3), shrine);

        Assert.AreEqual(shrine, field.Attribute(SourcePosition, OrangeAuraId, entityId: 9));
    }

    [TestMethod]
    public void Attribute_TerrainShareStronger_CreditsTheTerrainType()
    {
        var (field, glowingTypeId, _, _) = BuildFieldWithGlowingTerrainAt(SourcePosition);
        field.AddEntitySource(5, new Vector3Int(SourcePosition.X + 3, SourcePosition.Y, 0), new AuraSourceComponent(OrangeAuraId, power: 8, size: 3), EntitySource(5));

        Assert.AreEqual(ActionSource.FromTerrain(glowingTypeId), field.Attribute(SourcePosition, OrangeAuraId, entityId: 9), "Terrain 8 against the entity's 2 at its edge.");
    }

    [TestMethod]
    public void Attribute_EntityTiesTheTerrain_CreditsTheEntity()
    {
        var (field, _, _, _) = BuildFieldWithGlowingTerrainAt(SourcePosition);
        var holder = EntitySource(5);
        field.AddEntitySource(5, SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3), holder);

        Assert.AreEqual(holder, field.Attribute(SourcePosition, OrangeAuraId, entityId: 9));
    }

    [TestMethod]
    public void Attribute_TwoTerrainTypesRadiateTheAura_CreditsTheAura()
    {
        var (field, _, terrain, world) = BuildFieldWithGlowingTerrainAt(SourcePosition);
        var otherGlowing = terrain.Register(GlowingTerrain with { Key = "test:glowing-too" });
        world.SetTerrain(SourcePosition.X + 1, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(otherGlowing, 0));

        Assert.AreEqual(ActionSource.FromAura(OrangeAuraId), field.Attribute(SourcePosition, OrangeAuraId, entityId: 9));
    }

    [TestMethod]
    public void Attribute_TheEntitysOwnSourceNeverCreditsItself()
    {
        var (field, glowingTypeId, _, _) = BuildFieldWithGlowingTerrainAt(new Vector3Int(SourcePosition.X + 3, SourcePosition.Y, 0));
        field.AddEntitySource(5, SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3), EntitySource(5));

        Assert.AreEqual(ActionSource.FromTerrain(glowingTypeId), field.Attribute(SourcePosition, OrangeAuraId, entityId: 5), "Its own 8 is passed over for the terrain's 2.");
    }

    [TestMethod]
    public void Attribute_TwoEntitySourcesEqual_CreditsTheLowerKey()
    {
        var field = BuildField();
        var first = EntitySource(3);
        var second = EntitySource(7);
        field.AddEntitySource(7, new Vector3Int(SourcePosition.X + 1, SourcePosition.Y, 0), new AuraSourceComponent(OrangeAuraId, power: 8, size: 3), second);
        field.AddEntitySource(3, new Vector3Int(SourcePosition.X - 1, SourcePosition.Y, 0), new AuraSourceComponent(OrangeAuraId, power: 8, size: 3), first);

        Assert.AreEqual(first, field.Attribute(SourcePosition, OrangeAuraId, entityId: 9));
    }

    [TestMethod]
    public void Attribute_FollowsAMovedEntitySource_AndForgetsARemovedOne()
    {
        var field = BuildField();
        var mover = EntitySource(5);
        var source = new AuraSourceComponent(OrangeAuraId, power: 8, size: 3);
        field.AddEntitySource(5, new Vector3Int(60, 60, 0), source, mover);

        field.MoveEntitySource(5, new Vector3Int(60, 60, 0), SourcePosition, source);
        Assert.AreEqual(mover, field.Attribute(SourcePosition, OrangeAuraId, entityId: 9));

        field.RemoveEntitySource(5, SourcePosition, source);
        Assert.AreEqual(ActionSource.FromAura(OrangeAuraId), field.Attribute(SourcePosition, OrangeAuraId, entityId: 9));
    }

    /// <summary>An aura an entity set down on a tile reaches its placer like anyone else, and is credited to it -- unlike a source it carries. An aura that shouldn't hit its placer grants it an immunity first.</summary>
    [TestMethod]
    public void AnAnchorItPlaced_ReachesAndIsCreditedToItsPlacer()
    {
        var field = BuildField();
        var placer = EntitySource(5);
        field.AddEntitySource(6, SourcePosition, new AuraSourceComponent(OrangeAuraId, power: 8, size: 3), placer);

        Assert.AreEqual(8, field.PowerReaching(SourcePosition, OrangeAuraId, entityId: 5));
        Assert.AreEqual(placer, field.Attribute(SourcePosition, OrangeAuraId, entityId: 5));
    }
}
