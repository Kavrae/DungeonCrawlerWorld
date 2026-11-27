using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Actions.Components;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Auras.Systems;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.ProcessingTier.Components;
using Game.Spawning;
using Game.Views;
using Game.World;

namespace Tests.GameTests.Modules.Auras;

/// <summary>PLAN-aura-scaling.md, Phase 3: sources on entities that can move pulse instead of sitting in the field.</summary>
[TestClass]
public sealed class AuraPulseTests
{
    private const int GroundLayer = 1;
    private const ushort HealingPower = 16;
    private const byte HealingSize = 4;

    private static readonly Vector3Int CarrierPosition = new(10, 10, GroundLayer);
    private static readonly Vector3Int TargetPosition = new(11, 10, GroundLayer);

    /// <summary>Far enough from the carrier for the Neighborhood tier, in the same neighborhood, so it is still simulated.</summary>
    private static readonly Vector3Int NeighborhoodTierReference = new(CarrierPosition.X + 200, CarrierPosition.Y, GroundLayer);

    private sealed record Session(GameBuildPassResult Build)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public long Frame => Build.Context.SimulationClock.CurrentFrame;

        public int Spawn(Guid blueprintId, Vector3Int position) =>
            Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });

        /// <summary>A goblin that can move but decides nothing, so it stays where it was put.</summary>
        public int StandingGoblin(Vector3Int position)
        {
            var goblin = Spawn(Goblin.Id, position);
            Components.GetPackedPool<NpcDecisionComponent>().Remove(goblin);
            return goblin;
        }

        public int Chest(Vector3Int position, float health)
        {
            var chest = Spawn(TreasureChest.Id, position);
            var pool = Components.GetPackedPool<SimpleHealthComponent>();
            pool.TrySet(chest, pool.GetReadonly(chest) with { CurrentHealth = health });
            return chest;
        }

        public void GiveHealingSource(int entityId) =>
            Components.GetMultiPool<AuraSourceComponent>().Add(entityId, new AuraSourceComponent(HealingAuraId, HealingPower, HealingSize));

        public byte HealingAuraId => Build.Context.Auras.GetId(BuiltInAuraIds.HealingShrine);

        public float HealthOf(int entityId) => Components.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId).CurrentHealth;

        public bool IsExposedTo(int entityId, byte auraId) => Components.GetMultiPool<AuraExposureComponent>().CountMatching(entityId, new AuraExposureMatcher(auraId)) > 0;

        public void RunFrames(int count)
        {
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / AuraEffects.TickIntervalFrames);
            var start = Build.EcsContext.SystemManager.Clock.CurrentFrame + 1;
            for (var frame = start; frame < start + count; frame++)
            {
                Build.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
            }
        }

        /// <summary>How many frames of the next count raised entityId's health.</summary>
        public int CountHeals(int entityId, int count)
        {
            var heals = 0;
            for (var frame = 0; frame < count; frame++)
            {
                var before = HealthOf(entityId);
                RunFrames(1);
                if (HealthOf(entityId) > before)
                {
                    heals++;
                }
            }

            return heals;
        }
    }

    private static Session BuildSession(Vector3Int referencePosition)
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), sessionSeed: 1, initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(referencePosition);
        return new Session(build);
    }

    [TestMethod]
    public void ACarriedSource_IsNotInTheField_ReachesATargetWithinASecond_AndNeverItsCarrier()
    {
        var session = BuildSession(CarrierPosition);
        var carrier = session.StandingGoblin(CarrierPosition);
        var target = session.Chest(TargetPosition, health: 10);
        session.GiveHealingSource(carrier);

        Assert.AreEqual(0, session.Build.Context.AuraField.GetTotalPowerAt(CarrierPosition, session.HealingAuraId), "A source on an entity that can move pulses; it is never in the field.");
        session.RunFrames(2 * AuraEffects.TickIntervalFrames);

        Assert.IsGreaterThan(10f, session.HealthOf(target));
        Assert.IsFalse(session.IsExposedTo(carrier, session.HealingAuraId), "A source never affects its carrier.");
    }

    [TestMethod]
    public void ANeighborhoodTierCarrier_PulsingEveryFourSeconds_StillHealsItsTargetEverySecond()
    {
        var session = BuildSession(NeighborhoodTierReference);
        var carrier = session.StandingGoblin(CarrierPosition);
        var target = session.Chest(TargetPosition, health: 1);
        session.GiveHealingSource(carrier);
        session.RunFrames(AuraSystem.NeighborhoodPulsePeriodFrames + AuraEffects.TickIntervalFrames);

        var heals = session.CountHeals(target, 4 * AuraEffects.TickIntervalFrames);

        Assert.AreEqual(4, heals, "One heal a second for four seconds, from a carrier that pulsed once or twice in that time.");
    }

    [TestMethod]
    public void ACarriedSourceTurnedOff_StopsWithinOnePeriod()
    {
        var session = BuildSession(CarrierPosition);
        var carrier = session.StandingGoblin(CarrierPosition);
        var target = session.Chest(TargetPosition, health: 1);
        session.GiveHealingSource(carrier);
        session.RunFrames(2 * AuraEffects.TickIntervalFrames);

        AuraSourceEffects.Revoke(session.Components.GetMultiPool<AuraSourceComponent>(), session.Build.EcsContext.EventBus, carrier, session.HealingAuraId);
        session.RunFrames(AuraSystem.LocalPulsePeriodFrames + AuraEffects.TickIntervalFrames + 1);

        Assert.AreEqual(0, session.CountHeals(target, 3 * AuraEffects.TickIntervalFrames), "The record lasted at most one period past the last pulse.");
    }

    [TestMethod]
    public void ACarrierDroppingFromLocalToNeighborhood_ItsTargetMissesNoSecond()
    {
        var session = BuildSession(CarrierPosition);
        var carrier = session.StandingGoblin(CarrierPosition);
        var target = session.Chest(TargetPosition, health: 1);
        session.GiveHealingSource(carrier);
        session.RunFrames(2 * AuraEffects.TickIntervalFrames);

        RetierBoth(session, NeighborhoodTierReference, carrier, target);
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, TierOf(session, carrier), "Precondition: the carrier dropped to the Neighborhood tier.");
        var healsPerSecond = new List<int>();
        for (var second = 0; second < 6; second++)
        {
            healsPerSecond.Add(session.CountHeals(target, AuraEffects.TickIntervalFrames));
        }

        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1, 1, 1 }, healsPerSecond, "A heal in every second across the tier change.");
    }

    /// <summary>A carrier rising from the Neighborhood tier to Local pulses at once, so a target that comes into its reach then waits a second, not the rest of a four-second period.</summary>
    [TestMethod]
    public void ACarrierRisingFromNeighborhoodToLocal_PulsesAtOnce_SoANewTargetIsReachedWithinASecond()
    {
        var session = BuildSession(NeighborhoodTierReference);
        var carrier = session.StandingGoblin(CarrierPosition);
        session.GiveHealingSource(carrier);
        var pulses = session.Build.Context.AuraPulses;
        session.RunFrames(2 * AuraSystem.NeighborhoodPulsePeriodFrames);
        var pulsesBefore = pulses.Pulses;
        for (var frame = 0; frame < AuraSystem.NeighborhoodPulsePeriodFrames && pulses.Pulses == pulsesBefore; frame++)
        {
            session.RunFrames(1);
        }

        var target = session.Chest(TargetPosition, health: 1);
        RetierBoth(session, CarrierPosition, carrier, target);
        Assert.AreEqual(ProcessingTierLevel.Local, TierOf(session, carrier), "Precondition: the carrier rose to Local, just after a Neighborhood pulse.");

        Assert.IsGreaterThan(0, session.CountHeals(target, 2 * AuraEffects.TickIntervalFrames), "Reached within two seconds, though the carrier's last Neighborhood pulse was just now.");
    }

    /// <summary>Carriers added in one frame keep pulsing every period: a first pulse staggered up to one and a half periods out still lands on its own frame.</summary>
    [TestMethod]
    public void ManyCarriersAddedAtOnce_EachPulsesEveryPeriod()
    {
        const int carrierCount = 50;
        const int periods = 3;
        var session = BuildSession(NeighborhoodTierReference);
        for (var index = 0; index < carrierCount; index++)
        {
            var carrier = session.StandingGoblin(new Vector3Int(2 + (index % 10) * 3, 2 + (index / 10) * 3, GroundLayer));
            session.GiveHealingSource(carrier);
        }

        var pulses = session.Build.Context.AuraPulses;
        session.RunFrames(AuraSystem.NeighborhoodPulsePeriodFrames * 3 / 2);
        var pulsesBefore = pulses.Pulses;
        session.RunFrames(periods * AuraSystem.NeighborhoodPulsePeriodFrames);

        Assert.AreEqual(carrierCount * periods, pulses.Pulses - pulsesBefore, "Every carrier pulsed once a period.");
    }

    /// <summary>What a tile is in reads the field and the pulse sources alike: a carried aura's power at a tile, and its carrier credited.</summary>
    [TestMethod]
    public void AuraView_AtATileInACarriedAurasReach_ReportsItsPower_AndItsCarrier()
    {
        var session = BuildSession(CarrierPosition);
        var carrier = session.StandingGoblin(CarrierPosition);
        session.GiveHealingSource(carrier);
        var auraView = ViewOf(session);
        var aurasAtTile = new List<AuraAtCell>();

        auraView.CopyAurasAt(TargetPosition.X, TargetPosition.Y, TargetPosition.Z, aurasAtTile);

        Assert.AreEqual(13, auraView.PowerAt(TargetPosition.X, TargetPosition.Y, TargetPosition.Z, session.HealingAuraId), "Power 16 at reach 4, one tile out.");
        Assert.HasCount(1, aurasAtTile);
        Assert.AreEqual(session.Build.Context.EntityKeys.GetKey(carrier), aurasAtTile[0].Credit.Key);
        Assert.IsTrue(auraView.AnyAuraReaches(TargetPosition.X, TargetPosition.Y, TargetPosition.Z));
    }

    /// <summary>The pulse sources an area is drawn with are the ones reaching it, and a tile's version moves when a carried aura moves onto it.</summary>
    [TestMethod]
    public void AuraView_PulseSourcesReachingAnArea_AreThoseNearIt_AndATilesVersionFollowsThem()
    {
        var session = BuildSession(CarrierPosition);
        var carrier = session.StandingGoblin(CarrierPosition);
        session.GiveHealingSource(carrier);
        var auraView = ViewOf(session);
        var sources = new List<PulseSourceReach>();

        auraView.CopyPulseSourcesReaching(0, 0, 20, 20, GroundLayer, sources);
        Assert.HasCount(1, sources);
        auraView.CopyPulseSourcesReaching(30, 30, 40, 40, GroundLayer, sources);
        Assert.IsEmpty(sources, "Nothing it carries reaches that far.");

        var farTile = new Vector3Int(CarrierPosition.X + 8, CarrierPosition.Y, GroundLayer);
        var versionBefore = auraView.VersionAt(farTile.X, farTile.Y, farTile.Z);
        Assert.IsTrue(TeleporterOf(session).TryTeleport(carrier, new Vector3Int(CarrierPosition.X + 5, CarrierPosition.Y, GroundLayer)));

        Assert.AreNotEqual(versionBefore, auraView.VersionAt(farTile.X, farTile.Y, farTile.Z), "The carried aura now reaches the tile.");
    }

    private static AuraView ViewOf(Session session) => new(session.Build.Context.AuraField, session.Build.Context.AuraPulses, session.Build.Context.Auras);

    private static EntityTeleporter TeleporterOf(Session session)
    {
        var components = session.Components;
        return new EntityTeleporter(session.Build.World, components.GetDirectPool<Game.Modules.Core.Components.TransformComponent>(), session.Build.Factory.SpawnMoves,
            session.Build.EcsContext.EventBus, session.Build.Factory.Skeletons, components.GetPackedPool<MovementComponent>(), components.GetPackedPool<PendingWindupComponent>());
    }

    private static void RetierBoth(Session session, Vector3Int reference, int carrier, int target)
    {
        var resolver = session.Build.Context.ProcessingTierResolver;
        resolver.SetReferencePosition(reference);
        resolver.Retier(carrier);
        resolver.Retier(target);
    }

    private static ProcessingTierLevel TierOf(Session session, int entityId) =>
        session.Components.GetDirectPool<ProcessingTierComponent>().GetReadonly(entityId).Tier;

    [TestMethod]
    public void ATeleportedShrine_MovesItsFieldReachOnTheFrame_AndTheTileItLeftStopsHealing()
    {
        var session = BuildSession(CarrierPosition);
        var shrine = session.Spawn(HealingShrine.Id, CarrierPosition);
        var leftBehind = session.Chest(TargetPosition, health: 1);
        session.RunFrames(2 * AuraEffects.TickIntervalFrames);
        var destination = new Vector3Int(30, 30, GroundLayer);
        Assert.IsTrue(TeleporterOf(session).TryTeleport(shrine, destination));
        session.RunFrames(2);

        var healingAuraId = session.HealingAuraId;
        Assert.IsGreaterThan(0, session.Build.Context.AuraField.GetTotalPowerAt(destination, healingAuraId));
        Assert.AreEqual(0, session.Build.Context.AuraField.GetTotalPowerAt(TargetPosition, healingAuraId));
        session.RunFrames(AuraEffects.TickIntervalFrames);
        Assert.AreEqual(0, session.CountHeals(leftBehind, 2 * AuraEffects.TickIntervalFrames));
    }
}
