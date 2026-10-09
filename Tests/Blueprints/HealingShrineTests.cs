using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Objects;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Auras.Components;
using Game.Modules.Burning.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Movement.Components;
using Game.Modules.StatModifiers.Components;
using Game.Resources;
using Game.Spawning;
using Game.Tags;
using Game.Terrain;
using Game.World;

namespace Tests.Blueprints;

/// <summary>The Healing Shrine and its aura, in a build of every built-in module: spawned through the factory and run through the real systems.</summary>
[TestClass]
public sealed class HealingShrineTests
{
    private const int FramesPerSecond = 60;
    private const int GroundLayer = (int)MapLayer.Ground;

    private static readonly Vector3Int ShrinePosition = new(10, 10, GroundLayer);

    private sealed record Session(GameBuildPassResult Build)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public int Spawn(Guid blueprintId, Vector3Int position) =>
            Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });

        public float HealthOf(int entityId) => Components.GetPackedPool<SimpleHealthComponent>().GetReadonly(entityId).CurrentHealth;

        public void SetHealth(int entityId, float currentHealth) =>
            Components.GetPackedPool<SimpleHealthComponent>().TryUpdate(entityId, currentHealth, static (ref SimpleHealthComponent health, float value) => health.CurrentHealth = value);

        public void Damage(int entityId, ushort amount, bool fire = false, BodyPartTargetRule? targetRule = null) =>
            HealthDamage.Apply(
                Components.GetPackedPool<SimpleHealthComponent>(), Build.EcsContext.EventBus, entityId, amount, ActionSource.Admin, Build.World, "Test", Build.Context.SimulationClock.CurrentFrame,
                Components.GetMultiPool<StatModifierComponent>(), EntityBodyParts.For(Components, Build.Context.Definitions), Build.Context.MathUtility,
                Components.GetPackedPool<DeadComponent>(), Build.Context.EffectServices.DamageLedger, Build.Context.FloatingTextFeed, ResourceLossCategory.Direct, targetRule, damageTags: fire ? [GameTags.DamageFire] : default);

        public void RunFrames(int count)
        {
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
            var start = Build.EcsContext.SystemManager.Clock.CurrentFrame + 1;
            for (var frame = start; frame < start + count; frame++)
            {
                Build.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
            }
        }

        /// <summary>Runs until entityId's health changes, for a test that then counts whole seconds from a tick.</summary>
        public void RunUntilHealthChanges(int entityId)
        {
            var healthBefore = HealthOf(entityId);
            for (var frame = 0; frame < 3 * FramesPerSecond && HealthOf(entityId) == healthBefore; frame++)
            {
                RunFrames(1);
            }
        }
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(ShrinePosition);
        return new Session(build);
    }

    private static Vector3Int Beside(Vector3Int position, int tilesEast) => new(position.X + tilesEast, position.Y, position.Z);

    [TestMethod]
    public void Spawned_RadiatesAPowerSixteenSizeFourHealingAura()
    {
        var session = BuildSession();

        var shrineId = session.Spawn(HealingShrine.Id, ShrinePosition);

        var sources = session.Components.GetMultiPool<AuraSourceComponent>();
        Assert.AreEqual(1, sources.CountForEntity(shrineId));
        var source = sources.GetReadonlyByDenseIndex(sources.GetFirstDenseIndex(shrineId));
        Assert.AreEqual(session.Build.Context.Auras.GetId(HealingShrine.Aura.Id), source.AuraId);
        Assert.AreEqual(16, source.Power);
        Assert.AreEqual(4, source.Size);
    }

    [TestMethod]
    [DataRow(1, 13)]
    [DataRow(2, 10)]
    [DataRow(3, 7)]
    [DataRow(4, 4)]
    public void HealsAnEntityInRangeByTheAurasPowerThereEachSecond(int tilesAway, int healthPerSecond)
    {
        var session = BuildSession();
        session.Spawn(HealingShrine.Id, ShrinePosition);
        var chestId = session.Spawn(TreasureChest.Id, Beside(ShrinePosition, tilesAway));
        session.SetHealth(chestId, 50);

        session.RunUntilHealthChanges(chestId);
        Assert.AreEqual(50 + healthPerSecond, session.HealthOf(chestId));

        session.RunFrames(2 * FramesPerSecond);
        Assert.AreEqual(50 + 3 * healthPerSecond, session.HealthOf(chestId));
    }

    /// <summary>Each tick's heal shows above the healed entity, the amount the health bar moved by.</summary>
    [TestMethod]
    public void EachHeal_PublishesItsAmountAsFloatingText()
    {
        var session = BuildSession();
        session.Spawn(HealingShrine.Id, ShrinePosition);
        var chestId = session.Spawn(TreasureChest.Id, Beside(ShrinePosition, 1));
        session.SetHealth(chestId, 50);
        var published = new List<FloatingTextEvent>();
        session.Build.EcsContext.EventBus.Subscribe<FloatingTextEvent>(published.Add);

        session.RunUntilHealthChanges(chestId);

        var healed = published.Single(text => text.EntityId == chestId);
        Assert.AreEqual(FloatingTextKind.Healed, healed.Kind);
        Assert.AreEqual(13, healed.Amount);
    }

    /// <summary>A creature with body parts gets the whole heal on its most damaged part, so nothing is spent on parts that are already full and the text shows the full amount.</summary>
    [TestMethod]
    public void CreatureWithBodyParts_WholeHealGoesToItsMostDamagedPart()
    {
        var session = BuildSession();
        session.Spawn(HealingShrine.Id, ShrinePosition);
        var goblinId = session.Spawn(Goblin.Id, Beside(ShrinePosition, 1));
        session.Components.GetPackedPool<MovementComponent>().Remove(goblinId);
        var bodyParts = EntityBodyParts.For(session.Components, session.Build.Context.Definitions);
        var torsoPartId = BodyPartSelection.PickByType(bodyParts, goblinId, BodyPartType.Torso);
        bodyParts.TryGet(goblinId, torsoPartId, out var torsoBefore);
        session.Damage(goblinId, 20, targetRule: new BodyPartTargetRule(BodyPartType.Torso, BodyPartFallback.Bottommost));
        var published = new List<FloatingTextEvent>();
        session.Build.EcsContext.EventBus.Subscribe<FloatingTextEvent>(published.Add);

        session.RunFrames(FramesPerSecond + 2);

        var healed = published.First(text => text.EntityId == goblinId && text.Kind == FloatingTextKind.Healed);
        Assert.AreEqual(13, healed.Amount);
        bodyParts.TryGet(goblinId, torsoPartId, out var torsoAfter);
        Assert.IsGreaterThanOrEqualTo(torsoBefore.CurrentHealth - 20 + 13, torsoAfter.CurrentHealth);
    }

    [TestMethod]
    public void OutOfRange_HealsNothing()
    {
        var session = BuildSession();
        session.Spawn(HealingShrine.Id, ShrinePosition);
        var chestId = session.Spawn(TreasureChest.Id, Beside(ShrinePosition, 5));
        session.SetHealth(chestId, 50);

        session.RunFrames(3 * FramesPerSecond);

        Assert.AreEqual(50, session.HealthOf(chestId));
    }

    [TestMethod]
    public void TwoShrinesOverlapping_TheirHealingAdds()
    {
        var session = BuildSession();
        session.Spawn(HealingShrine.Id, Beside(ShrinePosition, -1));
        session.Spawn(HealingShrine.Id, Beside(ShrinePosition, 1));
        var chestId = session.Spawn(TreasureChest.Id, ShrinePosition);
        session.SetHealth(chestId, 50);

        session.RunUntilHealthChanges(chestId);

        Assert.AreEqual(76, session.HealthOf(chestId));
    }

    [TestMethod]
    public void HealingStopsAtMaximumHealth()
    {
        var session = BuildSession();
        session.Spawn(HealingShrine.Id, ShrinePosition);
        var chestId = session.Spawn(TreasureChest.Id, Beside(ShrinePosition, 1));
        session.SetHealth(chestId, 97);

        session.RunFrames(3 * FramesPerSecond);

        Assert.AreEqual(100, session.HealthOf(chestId));
    }

    [TestMethod]
    public void DoesNotHealItself()
    {
        var session = BuildSession();
        var shrineId = session.Spawn(HealingShrine.Id, ShrinePosition);
        session.SetHealth(shrineId, 50);

        session.RunFrames(3 * FramesPerSecond);

        Assert.AreEqual(50, session.HealthOf(shrineId));
    }

    [TestMethod]
    public void TakesHalfDamage()
    {
        var session = BuildSession();
        var shrineId = session.Spawn(HealingShrine.Id, ShrinePosition);

        session.Damage(shrineId, 20);

        Assert.AreEqual(90, session.HealthOf(shrineId));
    }

    [TestMethod]
    public void TakesNoFireDamage()
    {
        var session = BuildSession();
        var shrineId = session.Spawn(HealingShrine.Id, ShrinePosition);

        session.Damage(shrineId, 20, fire: true);

        Assert.AreEqual(100, session.HealthOf(shrineId));
    }

    /// <summary>A shrine may be placed on lava: neither the contact damage nor the burn touches it, while a neighbour is both burned and healed.</summary>
    [TestMethod]
    public void StandingOnLava_IsNeitherDamagedNorBurned()
    {
        var session = BuildSession();
        var terrain = session.Build.Context.Terrain;
        session.Build.World.PopulateTerrain(ShrinePosition.X, ShrinePosition.Y, TerrainLayer.Ground, new TerrainCell(terrain.GetId(BuiltInTerrain.LavaKey), 0));
        var shrineId = session.Spawn(HealingShrine.Id, ShrinePosition);

        session.RunFrames(5 * FramesPerSecond);

        Assert.AreEqual(100, session.HealthOf(shrineId));
        Assert.IsFalse(session.Components.GetPackedPool<BurningTimerComponent>().Has(shrineId));
    }

    [TestMethod]
    public void Destroyed_ItsAuraEnds()
    {
        var session = BuildSession();
        var healingAuraId = session.Build.Context.Auras.GetId(HealingShrine.Aura.Id);
        var shrineId = session.Spawn(HealingShrine.Id, ShrinePosition);
        var beside = Beside(ShrinePosition, 1);
        session.RunFrames(2);
        Assert.AreEqual(13, session.Build.Context.AuraField.GetTotalPowerAt(beside, healingAuraId), "Precondition: the shrine's aura is in the field.");

        session.Damage(shrineId, 1000);
        session.RunFrames(2);

        Assert.IsTrue(session.Components.GetPackedPool<DeadComponent>().Has(shrineId));
        Assert.AreEqual(0, session.Build.Context.AuraField.GetTotalPowerAt(beside, healingAuraId));
        Assert.IsFalse(session.Build.Context.AuraField.TryGetGlow(beside, out _, out _));
    }

    /// <summary>Applying the shrine's blueprint to an entity already on the map gives it the aura, in the field at once, with nothing to announce it.</summary>
    [TestMethod]
    public void AppliedToALiveEntity_ThatEntityRadiatesTheAura()
    {
        var session = BuildSession();
        var healingAuraId = session.Build.Context.Auras.GetId(HealingShrine.Aura.Id);
        var chestId = session.Spawn(TreasureChest.Id, ShrinePosition);
        session.RunFrames(2);

        session.Build.Factory.Apply(chestId, session.Build.Context.Definitions.GetId(HealingShrine.Id));

        Assert.AreEqual(13, session.Build.Context.AuraField.GetTotalPowerAt(Beside(ShrinePosition, 1), healingAuraId));

        session.Build.Factory.Apply(chestId, session.Build.Context.Definitions.GetId(HealingShrine.Id));
        Assert.AreEqual(1, session.Components.GetMultiPool<AuraSourceComponent>().CountForEntity(chestId), "Applying it again adds no second source.");
    }
}
