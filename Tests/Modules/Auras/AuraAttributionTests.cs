using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Poison.Components;
using Game.Spawning;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Auras;

/// <summary>In a build of every built-in module, an aura's effect is credited to its strongest contributor -- an entity source's entity, an anchor's placer -- and a kill follows that credit.</summary>
[TestClass]
public sealed class AuraAttributionTests
{
    private const int FramesPerSecond = 60;
    private const int Ground = (int)MapLayer.Ground;

    private static readonly Vector3Int SourcePosition = new(10, 10, Ground);
    private static readonly Vector3Int VictimPosition = new(11, 10, Ground);

    private static readonly AuraDefinition Deadly = new(new Guid("00000000-0000-0000-0000-0000000000d1"), "Deadly", Color.Black,
        [new Effect([new DirectDamage(MinFlatDamage: 1000, MaxFlatDamage: 1000)])], Magnitude: AuraMagnitude.Flat);

    private sealed record Session(GameBuildPassResult Build)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public int Spawn(Guid blueprintId, Vector3Int position)
        {
            var entityId = Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });
            Components.GetPackedPool<MovementComponent>().Remove(entityId);
            return entityId;
        }

        public bool IsCreditedTo(ActionSource source, int entityId) => source.IsEntity(Build.EcsContext.EntityManager.Keys.GetKey(entityId));

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
        build.Context.ProcessingTierResolver.SetReferencePosition(SourcePosition);
        return new Session(build);
    }

    [TestMethod]
    public void KilledByAnAuraAnEntityRadiates_TheKillIsCreditedToThatEntity()
    {
        var session = BuildSession();
        var holderId = session.Spawn(Goblin.Id, SourcePosition);
        var victimId = session.Spawn(Goblin.Id, VictimPosition);
        var auraId = session.Build.Context.Auras.Register(Deadly);

        session.Components.GetMultiPool<AuraSourceComponent>().Add(holderId, new AuraSourceComponent(auraId, power: 1, size: 2));
        session.RunFrames(2 * FramesPerSecond);

        Assert.IsTrue(session.Components.GetPackedPool<DeadComponent>().TryGetReadonly(victimId, out var dead), "Precondition: the aura killed it.");
        Assert.IsTrue(session.IsCreditedTo(dead.KilledBy, holderId));
        Assert.IsFalse(session.Components.GetPackedPool<DeadComponent>().Has(holderId), "Its own aura never touches the holder.");
    }

    [TestMethod]
    public void AuraSetDownInGroundMode_IsCreditedToWhoeverPlacedIt()
    {
        var session = BuildSession();
        var casterId = session.Spawn(Goblin.Id, SourcePosition);
        var victimId = session.Spawn(Goblin.Id, VictimPosition);
        ActionGrantEffects.Grant(session.Components, session.Build.Context.Actions, casterId, ToxicAuraAction.Id, overrideDefinition: null);
        session.Components.GetPackedPool<ManaComponent>().Remove(casterId);
        session.Components.GetPackedPool<ManaComponent>().Add(casterId, new ManaComponent(50, 50));
        session.RunFrames(FramesPerSecond);

        var selection = session.Build.Context.TargetResolution.Select(casterId, ToxicAuraAction.Build().Activator, TargetingMode.Ground, SourcePosition, session.Build.Context.SimulationClock.CurrentFrame);
        session.Components.Merge(casterId, new PendingActionActivationComponent(ToxicAuraAction.Id, selection));
        var poisons = session.Components.GetPackedPool<PoisonTimerComponent>();
        for (var frame = 0; frame < 3 * FramesPerSecond && !poisons.Has(victimId); frame++)
        {
            session.RunFrames(1);
        }

        Assert.IsTrue(session.Components.GetPackedPool<AuraAnchorComponent>().Count > 0, "Precondition: the cloud was set down.");
        Assert.IsTrue(poisons.TryGetReadonly(victimId, out var poison), "Precondition: the cloud poisoned the goblin beside it.");
        Assert.IsTrue(session.IsCreditedTo(poison.Source, casterId));
        Assert.IsFalse(poisons.Has(casterId), "The cloud set down at its feet reaches the caster, but Toxic Aura holds a Poison immunity on its user.");

        session.RunFrames(5 * FramesPerSecond);
        Assert.IsTrue(session.Components.GetPackedPool<DeadComponent>().TryGetReadonly(victimId, out var dead), "Precondition: the cloud killed it.");
        Assert.IsTrue(session.IsCreditedTo(dead.KilledBy, casterId), "The kill follows the credit to whoever set the cloud down.");
    }
}
