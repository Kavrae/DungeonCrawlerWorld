using Engine.ECS.Components;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.World;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class TargetResolutionTests
{
    private const int Ground = (int)MapLayer.Ground;
    private const long Now = 7;

    private static readonly Vector3Int CasterPosition = new(5, 5, Ground);
    private static readonly Vector3Int TargetPosition = new(8, 5, Ground);

    private static readonly IActionActivator SingleTargetRangeEight = Immediate(new TargetingSpec(TargetShape.SingleTarget, Range: 8));
    private static readonly IActionActivator BurstRangeEightAreaOne = Immediate(new TargetingSpec(TargetShape.Burst, Range: 8, AreaSize: 1));
    private static readonly IActionActivator PotionLike = Immediate(new TargetingSpec(TargetShape.Burst, Range: 3, AreaSize: 1, TargetModeAffects: TargetModeAffects.MarkedOnly));

    private static IActionActivator Immediate(TargetingSpec spec) => new SpellActivator(spec, new ActionTiming(ActionTimingCategory.Immediate));

    private sealed record Session(GameBuildPassResult Build, int CasterId)
    {
        public TargetResolution Resolution => Build.Context.TargetResolution;

        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public int Spawn(Guid blueprintId, Vector3Int position, Vector2Byte? size = null)
        {
            var entityId = Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1, Size = size });
            Components.GetPackedPool<MovementComponent>().Remove(entityId);
            return entityId;
        }

        public void MoveTo(int entityId, Vector3Int position)
        {
            var transforms = Components.GetDirectPool<TransformComponent>();
            Build.World.MoveEntity(entityId, position, transforms.GetReadonly(entityId));
            transforms.TryUpdate(entityId, position, static (ref TransformComponent transform, Vector3Int moved) => transform.Position = moved);
        }

        public (List<Vector3Int> Tiles, ResolvedTargets Resolved) Resolve(IActionActivator activator, TargetSelection selection)
        {
            var tiles = new List<Vector3Int>();
            var resolved = Resolution.Resolve(CasterId, activator.Targeting, selection, tiles);
            return (tiles, resolved);
        }
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(CasterPosition);
        var session = new Session(build, CasterId: -1);
        return session with { CasterId = session.Spawn(Goblin.Id, CasterPosition) };
    }

    [TestMethod]
    public void PickMarked_PrefersABlockingEntity_ThenTiny_ThenPhasing()
    {
        var session = BuildSession();
        var phasingId = session.Spawn(PhasingFairy.Id, TargetPosition);
        var tinyId = session.Spawn(TinyGoblin.Id, TargetPosition);
        var blockingId = session.Spawn(Goblin.Id, TargetPosition);

        Assert.AreEqual(blockingId, session.Resolution.PickMarked(TargetPosition, Now));

        session.Build.EcsContext.EntityManager.DestroyEntity(blockingId);
        Assert.AreEqual(tinyId, session.Resolution.PickMarked(TargetPosition, Now));

        session.Build.EcsContext.EntityManager.DestroyEntity(tinyId);
        Assert.AreEqual(phasingId, session.Resolution.PickMarked(TargetPosition, Now));
    }

    [TestMethod]
    public void PickMarked_AmongSeveralTiny_PicksTheSameOneForTheSameTileAndFrame()
    {
        var session = BuildSession();
        int[] tinyIds = [session.Spawn(TinyGoblin.Id, TargetPosition), session.Spawn(TinyGoblin.Id, TargetPosition), session.Spawn(TinyGoblin.Id, TargetPosition)];

        var first = session.Resolution.PickMarked(TargetPosition, Now);

        CollectionAssert.Contains(tinyIds, first);
        Assert.AreEqual(first, session.Resolution.PickMarked(TargetPosition, Now));
    }

    [TestMethod]
    public void PickMarked_NeverADeadEntity()
    {
        var session = BuildSession();
        var corpseId = session.Spawn(Goblin.Id, TargetPosition);
        session.Components.Merge(corpseId, new DeadComponent(ActionSource.Admin, 0));

        Assert.IsNull(session.Resolution.PickMarked(TargetPosition, Now));
    }

    [TestMethod]
    public void TargetModeOnAnEmptyTile_MarksNothing_AndLandsOnTheTile()
    {
        var session = BuildSession();

        var selection = session.Resolution.Select(session.CasterId, SingleTargetRangeEight, TargetingMode.Target, TargetPosition, Now);
        var (tiles, resolved) = session.Resolve(SingleTargetRangeEight, selection);

        Assert.IsFalse(selection.HasMarkedEntity);
        CollectionAssert.AreEqual(new[] { TargetPosition }, tiles);
        Assert.IsNull(resolved.MarkedEntityId);
    }

    [TestMethod]
    public void TargetMode_FollowsTheMarkedEntityWhereverItMoves()
    {
        var session = BuildSession();
        var targetId = session.Spawn(Goblin.Id, TargetPosition);
        var selection = session.Resolution.Select(session.CasterId, BurstRangeEightAreaOne, TargetingMode.Target, TargetPosition, Now);
        var moved = new Vector3Int(9, 7, Ground);

        session.MoveTo(targetId, moved);
        var (tiles, resolved) = session.Resolve(BurstRangeEightAreaOne, selection);

        Assert.AreEqual(moved, resolved.Centre);
        Assert.AreEqual(targetId, resolved.MarkedEntityId);
        Assert.HasCount(5, tiles, "The Burst's area, centred on where the target is now.");
        CollectionAssert.DoesNotContain(tiles, TargetPosition);
    }

    [TestMethod]
    public void GroundMode_LandsOnTheAimedTile_WhoeverHasMovedSince()
    {
        var session = BuildSession();
        var targetId = session.Spawn(Goblin.Id, TargetPosition);
        var selection = session.Resolution.Select(session.CasterId, SingleTargetRangeEight, TargetingMode.Ground, TargetPosition, Now);

        session.MoveTo(targetId, new Vector3Int(9, 7, Ground));
        var (tiles, resolved) = session.Resolve(SingleTargetRangeEight, selection);

        CollectionAssert.AreEqual(new[] { TargetPosition }, tiles);
        Assert.IsNull(resolved.MarkedEntityId);
    }

    [TestMethod]
    public void TargetMode_TargetMovedOutOfRange_LandsOnTheFarthestTileInRangeTowardIt()
    {
        var session = BuildSession();
        var targetId = session.Spawn(Goblin.Id, TargetPosition);
        var selection = session.Resolution.Select(session.CasterId, SingleTargetRangeEight, TargetingMode.Target, TargetPosition, Now);

        session.MoveTo(targetId, new Vector3Int(25, 5, Ground));
        var (tiles, _) = session.Resolve(SingleTargetRangeEight, selection);

        CollectionAssert.AreEqual(new[] { new Vector3Int(CasterPosition.X + 8, 5, Ground) }, tiles);
    }

    [TestMethod]
    public void TargetMode_MarkedEntityDestroyed_LandsOnTheTileItWasMarkedOn()
    {
        var session = BuildSession();
        var targetId = session.Spawn(Goblin.Id, TargetPosition);
        var selection = session.Resolution.Select(session.CasterId, SingleTargetRangeEight, TargetingMode.Target, TargetPosition, Now);
        session.MoveTo(targetId, new Vector3Int(9, 7, Ground));

        session.Build.EcsContext.EntityManager.DestroyEntity(targetId);
        var (tiles, resolved) = session.Resolve(SingleTargetRangeEight, selection);

        CollectionAssert.AreEqual(new[] { TargetPosition }, tiles);
        Assert.IsNull(resolved.MarkedEntityId);
    }

    [TestMethod]
    public void TargetMode_MarkedEntityDied_LandsOnItsCorpse()
    {
        var session = BuildSession();
        var targetId = session.Spawn(Goblin.Id, TargetPosition);
        var selection = session.Resolution.Select(session.CasterId, SingleTargetRangeEight, TargetingMode.Target, TargetPosition, Now);
        var corpseTile = new Vector3Int(9, 7, Ground);
        session.MoveTo(targetId, corpseTile);

        session.Components.Merge(targetId, new DeadComponent(ActionSource.Admin, 0));
        var (tiles, resolved) = session.Resolve(SingleTargetRangeEight, selection);

        CollectionAssert.AreEqual(new[] { corpseTile }, tiles);
        Assert.AreEqual(targetId, resolved.MarkedEntityId);
    }

    [TestMethod]
    public void TargetMode_LargeEntity_KeepsTheFootprintTileThatWasAimedAt()
    {
        var session = BuildSession();
        var largeId = session.Spawn(Goblin.Id, new Vector3Int(6, 7, Ground), new Vector2Byte(2, 2));
        var aimed = new Vector3Int(7, 8, Ground);
        var selection = session.Resolution.Select(session.CasterId, SingleTargetRangeEight, TargetingMode.Target, aimed, Now);

        session.MoveTo(largeId, new Vector3Int(9, 7, Ground));
        var (tiles, _) = session.Resolve(SingleTargetRangeEight, selection);

        CollectionAssert.AreEqual(new[] { new Vector3Int(10, 8, Ground) }, tiles, "Its lower-right tile, where it was aimed, not its top-left.");
    }

    [TestMethod]
    public void GroundOnlySpec_IgnoresTheModeAndNeverMarks()
    {
        var session = BuildSession();
        session.Spawn(Goblin.Id, TargetPosition);
        var groundOnly = Immediate(new TargetingSpec(TargetShape.SingleTarget, Range: 8, Modes: TargetingModes.GroundOnly));

        var selection = session.Resolution.Select(session.CasterId, groundOnly, TargetingMode.Target, TargetPosition, Now);

        Assert.AreEqual(TargetingMode.Ground, selection.Mode);
        Assert.IsFalse(selection.HasMarkedEntity);
    }

    [TestMethod]
    public void MarkedOnly_TargetModeReachesOnlyTheMarkedEntity_GroundSplashes()
    {
        var session = BuildSession();
        var targetId = session.Spawn(Goblin.Id, TargetPosition);

        var (_, targeted) = session.Resolve(PotionLike, session.Resolution.Select(session.CasterId, PotionLike, TargetingMode.Target, TargetPosition, Now));
        var (groundTiles, grounded) = session.Resolve(PotionLike, session.Resolution.Select(session.CasterId, PotionLike, TargetingMode.Ground, TargetPosition, Now));

        Assert.IsTrue(targeted.MarkedOnly);
        Assert.AreEqual(targetId, targeted.MarkedEntityId);
        Assert.IsFalse(grounded.MarkedOnly);
        Assert.HasCount(5, groundTiles);
    }

    [TestMethod]
    public void SelectEntity_OnTheCaster_MarksTheCaster()
    {
        var session = BuildSession();

        var selection = session.Resolution.SelectEntity(session.CasterId, PotionLike, session.CasterId);
        var (_, resolved) = session.Resolve(PotionLike, selection);

        Assert.AreEqual(session.CasterId, resolved.MarkedEntityId);
        Assert.IsTrue(resolved.MarkedOnly);
    }

    [TestMethod]
    public void ImmediateActivation_LandsTheSameInBothModes_ForAnAreaSpec()
    {
        var session = BuildSession();
        session.Spawn(Goblin.Id, TargetPosition);

        var (targetTiles, _) = session.Resolve(BurstRangeEightAreaOne, session.Resolution.Select(session.CasterId, BurstRangeEightAreaOne, TargetingMode.Target, TargetPosition, Now));
        var (groundTiles, _) = session.Resolve(BurstRangeEightAreaOne, session.Resolution.Select(session.CasterId, BurstRangeEightAreaOne, TargetingMode.Ground, TargetPosition, Now));

        CollectionAssert.AreEquivalent(groundTiles, targetTiles);
    }

    [TestMethod]
    public void ScrollSelection_CarriesItsRangeAndAreaScaledByTheCastersIntelligence()
    {
        var session = BuildSession();
        AbilityScoreEffects.Grant(session.Components, session.CasterId, AbilityScoreType.Intelligence, baseValue: 150);
        var scroll = new ScrollActivator(new TargetingSpec(TargetShape.Burst, Range: 5, AreaSize: 3), new ActionTiming(ActionTimingCategory.Immediate), Guid.NewGuid());
        var expected = ScrollScalingEffects.ScaleTargeting(scroll.Targeting, ScrollScalingEffects.ComputeScaleMultiplier(150));

        var selection = session.Resolution.Select(session.CasterId, scroll, TargetingMode.Ground, TargetPosition, Now);

        Assert.IsGreaterThan(5, expected.Range, "Precondition: Intelligence 150 scales the scroll.");
        Assert.AreEqual(expected.Range, selection.Range);
        Assert.AreEqual(expected.AreaSize, selection.AreaSize);
    }
}
