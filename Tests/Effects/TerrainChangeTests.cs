using Engine.ECS.Components;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Spawning;
using Game.Terrain;
using Game.World;

namespace Tests.Effects;

/// <summary>A TerrainChange is placed on each resolved tile, goes through World.SetTerrain, and never touches a wall.</summary>
[TestClass]
public sealed class TerrainChangeTests
{
    private const int Ground = (int)MapLayer.Ground;
    private static readonly Vector3Int CasterPosition = new(5, 5, Ground);
    private static readonly IReadOnlyList<Effect> LavaEffects = [new Effect([new TerrainChange(BuiltInTerrain.LavaKey)])];

    private sealed record Session(GameBuildPassResult Build, int CasterId)
    {
        public Game.World.World World => Build.World;

        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public EffectContext Context() =>
            EffectContext.FromEntity(Build.Context.EffectServices, CasterId, CasterId, TestActivatables.Action(), Build.Context.SimulationClock.CurrentFrame);

        public bool IsLava(Vector3Int tile) => World.Map.GetTerrain(tile.X, tile.Y, TerrainLayer.Ground).TypeId == World.Terrain.GetId(BuiltInTerrain.LavaKey);

        public void BuildWall(Vector3Int tile) => World.SetStructure(tile, new TerrainCell(World.Terrain.GetId(BuiltInTerrain.StoneWallKey)));
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), sessionSeed: 1, initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(CasterPosition);
        var casterId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Goblin.Id), CasterPosition) with { Seed = 1 });
        return new Session(build, casterId);
    }

    [TestMethod]
    public void ApplyOnEachTile_TurnsEachFloorTileIntoTheTerrain_LeavingWallsAlone()
    {
        var session = BuildSession();
        var floor = new Vector3Int(10, 10, Ground);
        var wall = new Vector3Int(11, 10, Ground);
        session.BuildWall(wall);
        var changes = new List<TerrainChangedEvent>();
        session.Build.Context.EventBus.Subscribe<TerrainChangedEvent>(changes.Add);

        var outcome = EffectSequence.ApplyOnEachTile(LavaEffects, session.Context(), [floor, wall]);

        Assert.AreEqual(EffectOutcome.Applied, outcome);
        Assert.IsTrue(session.IsLava(floor));
        Assert.IsFalse(session.IsLava(wall));
        Assert.HasCount(1, changes);
        Assert.AreEqual((floor.X, floor.Y), (changes[0].X, changes[0].Y));
    }

    [TestMethod]
    public void ApplyOnEachTile_OnTerrainItAlreadyIs_ChangesNothing()
    {
        var session = BuildSession();
        var tile = new Vector3Int(10, 10, Ground);
        EffectSequence.ApplyOnEachTile(LavaEffects, session.Context(), [tile]);

        Assert.AreEqual(EffectOutcome.NoEffect, EffectSequence.ApplyOnEachTile(LavaEffects, session.Context(), [tile]));
    }

    [TestMethod]
    public void OnEachTileEntries_AreNotAppliedPerTargetOrOnce()
    {
        var session = BuildSession();
        var context = session.Context();

        EffectSequence.ApplyOnEachTarget(LavaEffects, context);
        EffectSequence.ApplyOnce(LavaEffects, context, markedEntityId: null, CasterPosition);
        EffectSequence.ApplyAtLocation(LavaEffects, context, CasterPosition);

        Assert.IsFalse(session.IsLava(CasterPosition));
    }

    [TestMethod]
    public void TerrainChange_WithNoTargetLocation_DoesNothing()
    {
        var session = BuildSession();

        Assert.AreEqual(EffectOutcome.NoEffect, new TerrainChange(BuiltInTerrain.LavaKey).Apply(session.Context()));
    }

    [TestMethod]
    public void RingOfLava_TurnsTheRingAroundTheAimIntoLava_AndNothingInside()
    {
        var session = BuildSession();
        var action = session.Build.Context.Actions.Get(BuiltInActionIds.RingOfLava);
        var aimedTile = new Vector3Int(12, 5, Ground);
        var wall = new Vector3Int(15, 5, Ground);
        session.BuildWall(wall);
        var resolution = session.Build.Context.TargetResolution;
        var selection = resolution.Select(session.CasterId, action.Activator, TargetingMode.Ground, aimedTile, now: 0);
        var tiles = new List<Vector3Int>();
        var resolved = resolution.Resolve(session.CasterId, action.Activator.Targeting, selection, tiles);

        ActionEffectResolver.Apply(action, session.CasterId, tiles, resolved, session.Build.Context.EffectServices, session.World, now: 0,
            session.Components.GetPackedPool<DodgingComponent>(), new ProcessingTierQuery(session.Components.GetDirectPool<ProcessingTierComponent>()));

        Assert.IsNotEmpty(tiles);
        Assert.IsTrue(tiles.Where(tile => tile != wall).All(session.IsLava));
        Assert.IsFalse(session.IsLava(wall));
        Assert.IsFalse(session.IsLava(aimedTile));
        Assert.IsFalse(session.IsLava(new Vector3Int(13, 5, Ground)));
    }
}
