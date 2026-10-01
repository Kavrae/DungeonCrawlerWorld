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
        Aura: new TerrainAura(OrangeAura, 8));

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

        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, strength: 8));

        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var glowColor, out var totalStrength));
        Assert.AreEqual(Color.Orange, glowColor);
        Assert.AreEqual(8, totalStrength);
        Assert.AreEqual(8, field.GetTotalStrengthAt(SourcePosition, OrangeAuraId));
    }

    [TestMethod]
    public void TryGetGlow_OutsideEverySourcesReach_ReturnsFalse()
    {
        var field = BuildField();
        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, strength: 8));

        Assert.IsFalse(field.TryGetGlow(new Vector3Int(SourcePosition.X + 50, SourcePosition.Y, SourcePosition.Z), out _, out _));
    }

    [TestMethod]
    public void RemoveSource_LeavesNoGlowBehind()
    {
        var field = BuildField();
        var source = new AuraSourceComponent(OrangeAuraId, strength: 8);
        field.AddSource(SourcePosition, source);

        field.RemoveSource(SourcePosition, source);

        Assert.IsFalse(field.TryGetGlow(SourcePosition, out _, out _));
        Assert.AreEqual(0, field.GetTotalStrengthAt(SourcePosition, OrangeAuraId));
    }

    [TestMethod]
    public void TwoAurasOverlapping_GlowIsTheirColoursWeightedByStrength()
    {
        var field = BuildField();
        field.AddSource(SourcePosition, new AuraSourceComponent(RedAuraId, strength: 8));
        field.AddSource(new Vector3Int(SourcePosition.X + 2, SourcePosition.Y, SourcePosition.Z), new AuraSourceComponent(GreenAuraId, strength: 4));

        // At the first source: strength 8 of Red (distance 0), strength 1 of Green (distance 2, 4 >> 2).
        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var glowColor, out var totalStrength));
        Assert.AreEqual(new Color((byte)(255 * 8 / 9f), (byte)(128 * 1 / 9f), (byte)0), glowColor);
        Assert.AreEqual(9, totalStrength);
    }

    /// <summary>One source radiating two auras: each keeps its own reach and colour, and taking one away leaves the other exactly as it was.</summary>
    [TestMethod]
    public void TwoAurasAtOnePosition_BlendAndAreRemovedIndependently()
    {
        var field = BuildField();
        var redSource = new AuraSourceComponent(RedAuraId, strength: 8);
        var greenSource = new AuraSourceComponent(GreenAuraId, strength: 2);
        field.AddSource(SourcePosition, redSource);
        field.AddSource(SourcePosition, greenSource);

        var threeAway = new Vector3Int(SourcePosition.X + 3, SourcePosition.Y, SourcePosition.Z);
        Assert.IsTrue(field.TryGetGlow(threeAway, out var edgeColor, out var edgeStrength));
        Assert.AreEqual(Color.Red, edgeColor, "Only the stronger aura reaches this far.");
        Assert.AreEqual(1, edgeStrength);

        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var blendedColor, out var blendedStrength));
        Assert.AreEqual(new Color((byte)(255 * 8 / 10f), (byte)(128 * 2 / 10f), (byte)0), blendedColor);
        Assert.AreEqual(10, blendedStrength);

        field.RemoveSource(SourcePosition, redSource);

        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var remainingColor, out var remainingStrength));
        Assert.AreEqual(Color.Green, remainingColor);
        Assert.AreEqual(2, remainingStrength);
        Assert.IsFalse(field.TryGetGlow(threeAway, out _, out _));
    }

    /// <summary>The "any aura here at all" answer covers exactly the cells some aura reaches, and follows each aura leaving a cell on its own.</summary>
    [TestMethod]
    public void AnyAuraReaches_IsTrueWhileAtLeastOneAuraReachesTheCell()
    {
        var field = BuildField();
        var redSource = new AuraSourceComponent(RedAuraId, strength: 8);
        var greenSource = new AuraSourceComponent(GreenAuraId, strength: 2);
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
        var source = new AuraSourceComponent(OrangeAuraId, strength: 8);
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
        var source = new AuraSourceComponent(OrangeAuraId, strength: 8);
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

        Assert.AreEqual(8, field.GetTotalStrengthAt(SourcePosition, OrangeAuraId));
        Assert.AreEqual(3, field.MaxScanRadius);
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
        field.TerrainAuraAdded += position => announced.Add($"added {position.X},{position.Y}");
        field.TerrainAuraRemoved += position => announced.Add($"removed {position.X},{position.Y}");

        world.SetTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(plain, 0));
        Assert.IsFalse(field.TryGetGlow(SourcePosition, out _, out _));

        world.SetTerrain(SourcePosition.X, SourcePosition.Y, TerrainLayer.UnderGround, new TerrainCell(glowing, 0));
        Assert.IsTrue(field.TryGetGlow(SourcePosition, out _, out _));

        CollectionAssert.AreEqual(new[] { "removed 10,10", "added 10,10" }, announced);
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

        Assert.IsTrue(field.TryGetGlow(acrossTheSeam, out _, out var totalStrength));
        Assert.AreEqual(2, totalStrength);
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

        Assert.AreEqual(0, field.GetTotalStrengthAt(acrossTheSeam, OrangeAuraId));
        Assert.IsFalse(field.AnyAuraReaches(acrossTheSeam));

        world.Map.LoadNeighborhood(0, 0);
        world.PopulateTerrain(1023, 5, TerrainLayer.UnderGround, glowing);
        eventBus.Publish(new TerrainLoadedEvent(Neighborhoods.AreaOf(0, 0, 1), [new TerrainAuraCell(new Vector3Int(1023, 5, (int)MapLayer.UnderGround), aura)]));

        Assert.AreEqual(2, field.GetTotalStrengthAt(acrossTheSeam, OrangeAuraId));
    }

    /// <summary>An entity source at the window's edge is taken out whole even though the rectangle around the loaded neighborhoods changed between adding and removing it.</summary>
    [TestMethod]
    public void UnboundedMap_SourceAddedBeforeTheNeighborLoadsAndRemovedAfter_LeavesNothingBehind()
    {
        var world = TestWorlds.Create(Map.Unbounded(depth: 1));
        world.Map.LoadNeighborhood(0, 0);
        var field = BuildField(world);
        var source = new AuraSourceComponent(OrangeAuraId, strength: 8);
        var sourcePosition = new Vector3Int(1023, 5, 0);
        var acrossTheSeam = new Vector3Int(1025, 5, 0);

        field.AddSource(sourcePosition, source);
        world.Map.LoadNeighborhood(1, 0);
        field.RemoveSource(sourcePosition, source);

        Assert.AreEqual(0, field.GetTotalStrengthAt(acrossTheSeam, OrangeAuraId));
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

    /// <summary>Replacing a terrain's definition with one that radiates a different strength, or nothing, swaps the reach of every loaded cell of that terrain.</summary>
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
        field.TerrainAuraAdded += position => announced.Add($"added {position.X},{position.Y}");
        field.TerrainAuraRemoved += position => announced.Add($"removed {position.X},{position.Y}");

        terrain.Register(GlowingTerrain with { Aura = new TerrainAura(OrangeAura, 2) });

        Assert.AreEqual(2, field.GetTotalStrengthAt(SourcePosition, OrangeAuraId));
        CollectionAssert.AreEqual(new[] { "removed 10,10", "added 10,10" }, announced);

        terrain.Register(GlowingTerrain with { Aura = null });

        Assert.IsFalse(field.AnyAuraReaches(SourcePosition));
    }

    /// <summary>The glow's colour is read from the aura's definition when it is asked for, so a replaced definition recolours what is already in the field and says the glow changed.</summary>
    [TestMethod]
    public void AuraDefinitionReplaced_TheGlowTakesItsNewColourAndTheVersionMoves()
    {
        var auras = BuildAuras();
        var field = new AuraField(TestWorlds.Create(new Map(MapSize)), new TerrainRegistry(), auras, new EventBus());
        field.AddSource(SourcePosition, new AuraSourceComponent(OrangeAuraId, strength: 8));
        var versionBefore = field.Version;

        auras.Register(new AuraDefinition(OrangeAuraGuid, "Orange", Color.Purple));

        Assert.IsTrue(field.TryGetGlow(SourcePosition, out var glowColor, out _));
        Assert.AreEqual(Color.Purple, glowColor);
        Assert.AreNotEqual(versionBefore, field.Version);
    }
}
