using Engine.ECS.Components;
using Engine.Math;
using Engine.Tiles;
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

    private static readonly IActionActivator SingleTargetRangeEight = Immediate(new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 8));
    private static readonly IActionActivator BurstRangeEightAreaOne = Immediate(new TargetingSpec(TargetShape.Circle, MaximumRange: 8, AreaSize: 1));
    private static readonly IActionActivator PotionLike = Immediate(new TargetingSpec(TargetShape.Circle, MaximumRange: 3, AreaSize: 1, TargetModeAffects: TargetModeAffects.MarkedOnly));

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
            transforms.TrySet(entityId, transforms.GetReadonly(entityId) with { Position = position });
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
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), sessionSeed: 1, initialEntityCapacity: 200, initialComponentCapacity: 100);
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
        Assert.HasCount(9, tiles, "The Burst's area, centred on where the target is now.");
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
    public void AimLineOfSight_AWallOnTheMapBetweenCasterAndAim_ResolvesNothing_AndIsNotAimable()
    {
        var session = BuildSession();
        var sighted = Immediate(new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 8, AimLineOfSight: LineOfSightRule.Required));
        var selection = session.Resolution.Select(session.CasterId, sighted, TargetingMode.Ground, TargetPosition, Now);
        Assert.IsTrue(session.Resolution.IsAimable(session.CasterId, sighted.Targeting, TargetPosition));

        var world = session.Build.World;
        world.SetStructure(new Vector3Int(CasterPosition.X + 2, 5, Ground), new Game.Terrain.TerrainCell(world.Terrain.GetId(Game.Terrain.BuiltInTerrain.StoneWallKey)));
        var (tiles, _) = session.Resolve(sighted, selection);

        Assert.IsEmpty(tiles);
        Assert.IsFalse(session.Resolution.IsAimable(session.CasterId, sighted.Targeting, TargetPosition));
        Assert.IsTrue(session.Resolution.IsAimable(session.CasterId, sighted.Targeting, new Vector3Int(CasterPosition.X, 8, Ground)));
    }

    [TestMethod]
    public void NoLineOfSightRule_AWallTile_IsNotTargetable_AndResolvesNothing()
    {
        var session = BuildSession();
        var wallTile = new Vector3Int(CasterPosition.X + 2, 5, Ground);
        var behindWall = new Vector3Int(CasterPosition.X + 3, 5, Ground);
        var world = session.Build.World;
        world.SetStructure(wallTile, new Game.Terrain.TerrainCell(world.Terrain.GetId(Game.Terrain.BuiltInTerrain.StoneWallKey)));
        var selection = session.Resolution.Select(session.CasterId, SingleTargetRangeEight, TargetingMode.Ground, wallTile, Now);

        var (tiles, _) = session.Resolve(SingleTargetRangeEight, selection);

        Assert.IsEmpty(tiles);
        Assert.IsFalse(session.Resolution.IsTargetableTile(wallTile));
        Assert.IsFalse(session.Resolution.IsAimable(session.CasterId, SingleTargetRangeEight.Targeting, wallTile));
        Assert.IsTrue(session.Resolution.IsTargetableTile(behindWall));
        Assert.IsTrue(session.Resolution.IsAimable(session.CasterId, SingleTargetRangeEight.Targeting, behindWall), "With no sight rule, the tile behind the wall is still aimable.");
    }

    [TestMethod]
    public void Projectile_HitCountOne_HitsTheFirstEntity_AndCentresItsShapeThere()
    {
        var session = BuildSession();
        var fireballLike = Immediate(new TargetingSpec(TargetShape.Circle, MaximumRange: 8, AreaSize: 1, Path: TargetPath.Projectile, ProjectileHitCount: 1));
        var nearId = session.Spawn(Goblin.Id, new Vector3Int(CasterPosition.X + 2, 5, Ground));
        session.Spawn(Goblin.Id, new Vector3Int(CasterPosition.X + 5, 5, Ground));
        var selection = session.Resolution.Select(session.CasterId, fireballLike, TargetingMode.Ground, new Vector3Int(CasterPosition.X + 6, 5, Ground), Now);

        var (tiles, resolved) = session.Resolve(fireballLike, selection);

        Assert.AreEqual(new Vector3Int(CasterPosition.X + 2, 5, Ground), resolved.Centre);
        CollectionAssert.AreEqual(new[] { nearId }, resolved.PathHitEntityIds.ToArray());
        Assert.HasCount(9, tiles);
        CollectionAssert.Contains(resolved.PathTiles.ToList(), new Vector3Int(CasterPosition.X + 1, 5, Ground));
    }

    [TestMethod]
    public void Projectile_StoppedBeforeTheMarkedEntity_DoesNotReachIt()
    {
        var session = BuildSession();
        var bolt = Immediate(new TargetingSpec(TargetShape.None, MaximumRange: 8, Path: TargetPath.Projectile, ProjectileHitCount: 1));
        session.Spawn(Goblin.Id, new Vector3Int(CasterPosition.X + 1, 5, Ground));
        session.Spawn(Goblin.Id, TargetPosition);
        var selection = session.Resolution.Select(session.CasterId, bolt, TargetingMode.Target, TargetPosition, Now);
        Assert.IsTrue(selection.HasMarkedEntity);

        var (_, resolved) = session.Resolve(bolt, selection);

        Assert.IsNull(resolved.MarkedEntityId);
        Assert.HasCount(1, resolved.PathHitEntityIds);
    }

    [TestMethod]
    public void Projectile_AimOutOfSight_FiresNothing()
    {
        var session = BuildSession();
        var bolt = Immediate(new TargetingSpec(TargetShape.None, MaximumRange: 8, Path: TargetPath.Projectile, ProjectileHitCount: 1, AimLineOfSight: LineOfSightRule.Required));
        session.Spawn(Goblin.Id, TargetPosition);
        var world = session.Build.World;
        world.SetStructure(new Vector3Int(CasterPosition.X + 2, 5, Ground), new Game.Terrain.TerrainCell(world.Terrain.GetId(Game.Terrain.BuiltInTerrain.StoneWallKey)));
        var selection = session.Resolution.Select(session.CasterId, bolt, TargetingMode.Ground, TargetPosition, Now);

        var (_, resolved) = session.Resolve(bolt, selection);

        Assert.IsEmpty(resolved.PathHitEntityIds);
        Assert.IsEmpty(resolved.PathTiles);
    }

    [TestMethod]
    public void MagicMissile_DamagesOnlyTheFirstGoblinInLine()
    {
        var session = BuildSession();
        var action = session.Build.Context.Actions.Get(BuiltInActionIds.MagicMissile);
        var nearId = session.Spawn(Goblin.Id, new Vector3Int(CasterPosition.X + 2, 5, Ground));
        var farId = session.Spawn(Goblin.Id, new Vector3Int(CasterPosition.X + 4, 5, Ground));
        var selection = session.Resolution.Select(session.CasterId, action.Activator, TargetingMode.Ground, new Vector3Int(CasterPosition.X + 6, 5, Ground), Now);
        var tiles = new List<Vector3Int>();
        var resolved = session.Resolution.Resolve(session.CasterId, action.Activator.Targeting, selection, tiles);

        ActivationEffectResolver.Apply(action, session.CasterId, tiles, resolved, session.Build.Context.EffectServices, session.Build.World, Now,
            session.Components.GetPackedPool<Game.Modules.Actions.Components.DodgingComponent>(),
            new Game.Modules.ProcessingTier.ProcessingTierQuery(session.Components.GetDirectPool<Game.Modules.ProcessingTier.Components.ProcessingTierComponent>()));

        var damageTaken = session.Components.GetMultiPool<Game.Modules.Health.Components.DamageContributionComponent>();
        Assert.IsTrue(damageTaken.Has(nearId));
        Assert.IsFalse(damageTaken.Has(farId));
        Assert.AreEqual(1, damageTaken.CountForEntity(nearId));
    }

    [TestMethod]
    public void TargetMode_TargetMovedNearerThanMinimumRange_LandsOnTheFirstTileFarEnoughPastIt()
    {
        var session = BuildSession();
        var mortar = Immediate(new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 8, MinimumRange: 3));
        var targetId = session.Spawn(Goblin.Id, TargetPosition);
        var selection = session.Resolution.Select(session.CasterId, mortar, TargetingMode.Target, TargetPosition, Now);

        session.MoveTo(targetId, new Vector3Int(CasterPosition.X + 1, 5, Ground));
        var (tiles, resolved) = session.Resolve(mortar, selection);

        CollectionAssert.AreEqual(new[] { new Vector3Int(CasterPosition.X + 3, 5, Ground) }, tiles);
        Assert.AreEqual(new Vector3Int(CasterPosition.X + 3, 5, Ground), resolved.Centre);
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
        var groundOnly = Immediate(new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 8, Modes: TargetingModes.GroundOnly));

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
        Assert.HasCount(9, groundTiles);
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
        var scroll = new ScrollActivator(new TargetingSpec(TargetShape.Circle, MaximumRange: 5, AreaSize: 3), new ActionTiming(ActionTimingCategory.Immediate), Guid.NewGuid());
        var expected = ScrollScalingEffects.ScaleTargeting(scroll.Targeting, ScrollScalingEffects.ComputeScaleMultiplier(150));

        var selection = session.Resolution.Select(session.CasterId, scroll, TargetingMode.Ground, TargetPosition, Now);

        Assert.IsGreaterThan(5, expected.MaximumRange, "Precondition: Intelligence 150 scales the scroll.");
        Assert.AreEqual(expected.MaximumRange, selection.MaximumRange);
        Assert.AreEqual(expected.AreaSize, selection.AreaSize);
    }
}
