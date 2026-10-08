using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.World;
using Game.Modules.Actions.Activators;

namespace Tests.Modules.Actions;

/// <summary>Fireball, a Delayed burst, cast in each targeting mode in a build of every built-in module: Target mode follows its target through the windup, Ground mode lands where it was aimed.</summary>
[TestClass]
public sealed class TargetingModeActivationTests
{
    private const int FramesPerSecond = 60;
    private const int Ground = (int)MapLayer.Ground;
    private const float CasterMana = 100;

    private static readonly Vector3Int CasterPosition = new(5, 5, Ground);
    private static readonly Vector3Int AimedTile = new(11, 5, Ground);

    private static readonly IActionActivator FireballActivator = FireballAction.Build().Activator;

    private sealed record Session(GameBuildPassResult Build, int CasterId)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public int Spawn(Guid blueprintId, Vector3Int position)
        {
            var entityId = Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });
            Components.GetPackedPool<MovementComponent>().Remove(entityId);
            return entityId;
        }

        public void MoveTo(int entityId, Vector3Int position)
        {
            var transforms = Components.GetDirectPool<TransformComponent>();
            Build.World.MoveEntity(entityId, position, transforms.GetReadonly(entityId));
            transforms.TryUpdate(entityId, position, static (ref TransformComponent transform, Vector3Int moved) => transform.Position = moved);
        }

        public float HealthOf(int entityId)
        {
            HealthQueries.TryGetTotals(Components.GetPackedPool<SimpleHealthComponent>(), EntityBodyParts.For(Components, Build.Context.Definitions), entityId, out var current, out _);
            return current;
        }

        public float ManaOf(int entityId) => Components.GetPackedPool<ManaComponent>().GetReadonly(entityId).CurrentMana;

        public void Cast(TargetingMode mode, Vector3Int aimedTile)
        {
            var activator = FireballActivator;
            var selection = Build.Context.TargetResolution.Select(CasterId, activator, mode, aimedTile, Build.Context.SimulationClock.CurrentFrame);
            Components.Merge(CasterId, new PendingActionActivationComponent(FireballAction.Id, selection));
        }

        public void RunFrames(int count)
        {
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
            var start = Build.EcsContext.SystemManager.Clock.CurrentFrame + 1;
            for (var frame = start; frame < start + count; frame++)
            {
                Build.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
            }
        }
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(CasterPosition);
        var session = new Session(build, CasterId: -1);
        var casterId = session.Spawn(Goblin.Id, CasterPosition);
        ActionGrantEffects.Grant(session.Components, session.Build.Context.Actions, casterId, FireballAction.Id, overrideDefinition: null);
        session.Components.Merge(casterId, new ManaComponent(CasterMana, CasterMana));
        session.RunFrames(FramesPerSecond);
        return session with { CasterId = casterId };
    }

    private static int WindupFrames(Session session) =>
        ((SpellActivator)FireballActivator).Timing.ActionLockFrames!.Value;

    [TestMethod]
    public void Fireball_WindsUpThreeSeconds_AndSpendsItsManaWhenTheWindupStarts()
    {
        var session = BuildSession();
        var manaBefore = session.ManaOf(session.CasterId);

        session.Cast(TargetingMode.Ground, AimedTile);
        session.RunFrames(1);

        Assert.AreEqual(180, WindupFrames(session));
        Assert.IsTrue(session.Components.GetPackedPool<PendingWindupComponent>().Has(session.CasterId), "Winding up.");
        Assert.AreEqual(manaBefore - FireballAction.ManaCost, session.ManaOf(session.CasterId), 0.5f);
    }

    [TestMethod]
    public void Fireball_TargetMode_FollowsTheMarkedGoblinThroughTheWindup()
    {
        var session = BuildSession();
        var targetId = session.Spawn(Goblin.Id, AimedTile);
        var bystanderId = session.Spawn(Goblin.Id, new Vector3Int(AimedTile.X, AimedTile.Y + 2, Ground));
        var targetHealthBefore = session.HealthOf(targetId);
        var bystanderHealthBefore = session.HealthOf(bystanderId);

        session.Cast(TargetingMode.Target, AimedTile);
        session.RunFrames(FramesPerSecond);
        session.MoveTo(targetId, new Vector3Int(9, 11, Ground));
        session.RunFrames(WindupFrames(session));

        Assert.IsLessThan(targetHealthBefore, session.HealthOf(targetId), "The blast followed the goblin it was cast at.");
        Assert.AreEqual(bystanderHealthBefore, session.HealthOf(bystanderId), "Two tiles from where the goblin was aimed at, but too far from where it went.");
    }

    [TestMethod]
    public void Fireball_GroundMode_HitsWhoeverWalksIn_AndMissesWhoeverWalksOut()
    {
        var session = BuildSession();
        var leaverId = session.Spawn(Goblin.Id, AimedTile);
        var arriverId = session.Spawn(Goblin.Id, new Vector3Int(AimedTile.X, AimedTile.Y + 6, Ground));
        var leaverHealthBefore = session.HealthOf(leaverId);
        var arriverHealthBefore = session.HealthOf(arriverId);

        session.Cast(TargetingMode.Ground, AimedTile);
        session.RunFrames(FramesPerSecond);
        session.MoveTo(leaverId, new Vector3Int(AimedTile.X + 4, AimedTile.Y + 4, Ground));
        session.MoveTo(arriverId, new Vector3Int(AimedTile.X, AimedTile.Y + 1, Ground));
        session.RunFrames(WindupFrames(session));

        Assert.AreEqual(leaverHealthBefore, session.HealthOf(leaverId), "Walked out of the blast.");
        Assert.IsLessThan(arriverHealthBefore, session.HealthOf(arriverId), "Walked into it.");
    }

    [TestMethod]
    public void Fireball_TargetMode_TargetLeavesRange_LandsAtTheEdgeOfRangeTowardIt()
    {
        var session = BuildSession();
        var targetId = session.Spawn(Goblin.Id, AimedTile);
        var targetHealthBefore = session.HealthOf(targetId);

        session.Cast(TargetingMode.Target, AimedTile);
        session.RunFrames(FramesPerSecond);
        session.MoveTo(targetId, new Vector3Int(30, 5, Ground));
        var tiles = new List<Vector3Int>();
        Assert.IsTrue(session.Build.Context.TargetResolution.Resolve(session.CasterId, FireballActivator.Targeting,
            session.Components.GetPackedPool<PendingWindupComponent>().GetReadonly(session.CasterId).Selection, tiles).Centre == new Vector3Int(CasterPosition.X + 10, 5, Ground));
        session.RunFrames(WindupFrames(session));

        Assert.AreEqual(targetHealthBefore, session.HealthOf(targetId), "Too far to reach: the blast stops at the edge of range.");
    }

    [TestMethod]
    public void Player_HasFireballBoundToSlotEight()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(new Vector3Int(5, 5, Ground));
        var playerId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Player.Id), new Vector3Int(5, 5, Ground)) with { Seed = 1 });
        var components = build.EcsContext.ComponentManager;

        Assert.IsTrue(EntityActions.For(components, build.Context.Actions, build.Context.Definitions).Has(playerId, FireballAction.Id));
        var bindings = components.GetMultiPool<ActionHotkeyBindingComponent>();
        var boundToSlotEight = false;
        for (var denseIndex = bindings.GetFirstDenseIndex(playerId); denseIndex != -1; denseIndex = bindings.GetNextDenseIndex(denseIndex))
        {
            var binding = bindings.GetReadonlyByDenseIndex(denseIndex);
            boundToSlotEight |= binding.Slot == HotkeySlot.Slot8 && binding.ActionId == FireballAction.Id;
        }

        Assert.IsTrue(boundToSlotEight);
    }
}
