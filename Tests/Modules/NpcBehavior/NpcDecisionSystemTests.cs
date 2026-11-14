using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Classes;
using Game.Blueprints.Composites;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Mana.Components;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Spawning;
using Game.Views;
using Game.World;

namespace Tests.Modules.NpcBehavior;

/// <summary>NpcDecisionSystem in a whole built-in session: who decides, what a creature does in each situation, and what the inspection view reports.</summary>
[TestClass]
public sealed class NpcDecisionSystemTests
{
    internal const int FramesPerSecond = 60;
    internal const int GroundLayer = (int)MapLayer.Ground;

    internal static readonly Vector3Int CreaturePosition = new(10, 10, GroundLayer);
    private static readonly Vector3Int FarAwayPosition = new(35, 35, GroundLayer);
    private static readonly Guid HumanNpcId = new("5e1f0a00-4c0d-4b1e-9a55-0000000000f1");

    internal sealed record Session(GameBuildPassResult Build, int PlayerId)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public List<ActivationUsedEvent> Used { get; } = [];

        public int Spawn(Guid blueprintId, Vector3Int position) =>
            Build.Factory.Spawn(SpawnRequest.At(Build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });

        public NpcDecisionView DecisionView => new(Components, Build.Context.NpcDecisions, Build.World, Build.Context.SimulationClock);

        public IEnumerable<ActivationUsedEvent> UsedBy(int entityId) => Used.Where(used => used.EntityId == entityId);

        public float HealthFractionOf(int entityId) =>
            HealthQueries.TryGetTotals(Components.GetPackedPool<SimpleHealthComponent>(), EntityBodyParts.For(Components, Build.Context.Definitions), Build.Context.EffectServices.StatModifiers, entityId, out var current, out var maximum)
                ? current / maximum
                : 1f;

        /// <summary>Hurts entityId's limbs and torso, never a vital part to zero, until it is under half health.</summary>
        public void HurtBelowHalf(int entityId)
        {
            HurtUntil(entityId, 0.45f);
            Assert.IsLessThan(0.5f, HealthFractionOf(entityId), "the setup hurts the creature below half health");
        }

        /// <summary>Hurts entityId three points at a time, spread over its parts so no vital part reaches zero, until its health fraction is at most healthFraction.</summary>
        public void HurtUntil(int entityId, float healthFraction)
        {
            BodyPartType[] partsToHurt = [BodyPartType.Torso, BodyPartType.Leg, BodyPartType.Head, BodyPartType.Arm, BodyPartType.Internal];
            for (var hit = 0; hit < 500 && HealthFractionOf(entityId) > healthFraction; hit++)
            {
                Hurt(entityId, partsToHurt[hit % partsToHurt.Length], 3);
            }
        }

        public void Hurt(int entityId, BodyPartType partType, ushort amount) =>
            HealthDamage.Apply(
                Components.GetPackedPool<SimpleHealthComponent>(), Build.EcsContext.EventBus, entityId, amount, ActionSource.Admin, Build.World, "Test", Build.Context.SimulationClock.CurrentFrame,
                Components.GetMultiPool<Game.Modules.StatModifiers.Components.StatModifierComponent>(), EntityBodyParts.For(Components, Build.Context.Definitions), Build.Context.RandomStreams.Get(Game.Effects.EffectServices.RandomStream),
                Components.GetPackedPool<Game.Modules.Death.Components.DeadComponent>(), Build.Context.EffectServices.DamageLedger, Build.Context.FloatingTextFeed, Game.Resources.ResourceLossCategory.Direct,
                new BodyPartTargetRule(partType, BodyPartFallback.Topmost));

        public void SetMana(int entityId, float mana)
        {
            var manaPool = Components.GetPackedPool<ManaComponent>();
            manaPool.TrySet(entityId, manaPool.GetReadonly(entityId) with { CurrentMana = mana });
        }

        public Vector3Int PositionOf(int entityId) => Components.GetDirectPool<TransformComponent>().GetReadonly(entityId).Position;

        public void RunFrames(int count, Action? afterEachFrame = null)
        {
            var frameDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesPerSecond);
            var start = Build.EcsContext.SystemManager.Clock.CurrentFrame + 1;
            for (var frame = start; frame < start + count; frame++)
            {
                Build.EcsContext.SystemManager.Update(new EngineTime(frameDuration * frame, frameDuration, false, frame));
                afterEachFrame?.Invoke();
            }
        }
    }

    internal static Session BuildSession(Vector3Int? playerPosition = null)
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(40, 40, 3)), sessionSeed: 1, initialEntityCapacity: 200, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(CreaturePosition);
        var playerId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Player.Id), playerPosition ?? FarAwayPosition) with { Seed = 1, Size = new Vector2Byte(1, 1) });
        build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(playerId);
        build.World.PlayerEntityId = playerId;

        var session = new Session(build, playerId);
        build.EcsContext.EventBus.Subscribe<ActivationUsedEvent>(session.Used.Add);
        return session;
    }

    internal static Vector3Int Beside(Vector3Int position) => position with { X = position.X + 1 };

    [TestMethod]
    public void HurtGoblinWithManaAPotionAndAScroll_CastsHealFirst()
    {
        var session = BuildSession();
        var goblin = session.Spawn(Goblin.Id, CreaturePosition);
        InventoryActions.AddItem(session.Components, goblin, BuiltInItemIds.HealthPotion, 1);
        InventoryActions.AddItem(session.Components, goblin, BuiltInItemIds.ScrollOfHealing, 1);
        session.HurtBelowHalf(goblin);

        session.RunFrames(2 * FramesPerSecond);

        var firstHeal = session.UsedBy(goblin).First();
        Assert.AreEqual(ActivatableKind.Action, firstHeal.Kind);
        Assert.AreEqual(BuiltInActionIds.Heal, firstHeal.DefinitionId);
    }

    [TestMethod]
    public void HurtGoblinWithoutMana_UsesAConsumableInstead()
    {
        var session = BuildSession();
        var goblin = session.Spawn(Goblin.Id, CreaturePosition);
        InventoryActions.AddItem(session.Components, goblin, BuiltInItemIds.HealthPotion, 1);
        session.HurtBelowHalf(goblin);
        session.SetMana(goblin, 0);

        session.RunFrames(2 * FramesPerSecond);

        var firstHeal = session.UsedBy(goblin).First();
        Assert.AreEqual(ActivatableKind.Item, firstHeal.Kind);
        Assert.IsTrue(firstHeal.DefinitionId == BuiltInItemIds.HealthPotion || firstHeal.DefinitionId == BuiltInItemIds.ScrollOfHealing);
    }

    [TestMethod]
    public void HurtGoblinWithNothingElse_CastsHealOnceWithItsTwoMana()
    {
        var session = BuildSession();
        var goblin = session.Spawn(Goblin.Id, CreaturePosition);
        session.HurtBelowHalf(goblin);
        session.HurtBelowHalf(goblin);

        session.RunFrames(FramesPerSecond);

        Assert.AreEqual(1, session.UsedBy(goblin).Count(used => used.DefinitionId == BuiltInActionIds.Heal));
        Assert.IsLessThan(1f, session.Components.GetPackedPool<ManaComponent>().GetReadonly(goblin).CurrentMana);
    }

    [TestMethod]
    public void HealthyGoblinHoldingAPotion_NeverUsesIt()
    {
        var session = BuildSession();
        var goblin = session.Spawn(Goblin.Id, CreaturePosition);
        InventoryActions.AddItem(session.Components, goblin, BuiltInItemIds.HealthPotion, 1);

        session.RunFrames(5 * FramesPerSecond);

        Assert.IsFalse(session.UsedBy(goblin).Any(used => used.Kind == ActivatableKind.Item));
    }

    [TestMethod]
    public void StationaryCreatureBesideAHostile_AttacksWithoutEverMoving()
    {
        var session = BuildSession(playerPosition: Beside(CreaturePosition));
        var stationary = session.Spawn(GoblinFairy.Id, CreaturePosition);

        session.RunFrames(5 * FramesPerSecond, () => Assert.AreEqual(CreaturePosition, session.PositionOf(stationary)));

        Assert.IsTrue(session.Components.GetPackedPool<NpcDecisionComponent>().Has(stationary));
        Assert.IsFalse(session.Components.GetPackedPool<MovementComponent>().Has(stationary));
        Assert.IsTrue(session.UsedBy(stationary).Any(used => used.DefinitionId == BuiltInActionIds.QuickAttack || used.DefinitionId == BuiltInActionIds.PowerAttack));
    }

    [TestMethod]
    public void TestDummyWithNobodyBeside_NeverSwings()
    {
        var session = BuildSession();
        var dummy = session.Spawn(TestDummyBlueprint.Id, CreaturePosition);

        session.RunFrames(5 * FramesPerSecond);

        Assert.IsFalse(session.UsedBy(dummy).Any());
    }

    [TestMethod]
    public void TestDummyBesideThePlayer_WindsUpPowerAttackAndSwingsOneAtATime()
    {
        var session = BuildSession(playerPosition: Beside(CreaturePosition));
        var dummy = session.Spawn(TestDummyBlueprint.Id, CreaturePosition);
        var windups = session.Components.GetPackedPool<PendingWindupComponent>();
        var windupsStarted = 0;
        var wasWindingUp = false;

        session.RunFrames(3 * FramesPerSecond, () =>
        {
            var isWindingUp = windups.TryGetReadonly(dummy, out var windup) && windup.Activatable == ActivatableReference.Action(BuiltInActionIds.PowerAttack);
            windupsStarted += isWindingUp && !wasWindingUp ? 1 : 0;
            wasWindingUp = isWindingUp;
        });

        Assert.IsGreaterThan(0, windupsStarted);
        Assert.IsLessThanOrEqualTo(3, windupsStarted, "a one-second windup can start at most three times in three seconds");
    }

    [TestMethod]
    public void HumanNpc_Decides_AndThePlayerDoesNot()
    {
        var session = BuildSession(playerPosition: CreaturePosition);
        session.Build.Context.Definitions.Register(new Game.Blueprints.BlueprintDefinition(HumanNpcId, "Test Human") { Includes = [Human.Id], Appearance = new Game.Blueprints.AppearanceFacet { Name = "Test Human", Description = "A human who is not the player." } });
        var human = session.Spawn(HumanNpcId, new Vector3Int(20, 20, GroundLayer));
        var decisions = session.Build.Context.NpcDecisions;
        var decisionsBefore = decisions.Decisions;

        session.RunFrames(3 * FramesPerSecond);

        Assert.IsTrue(session.Components.GetPackedPool<NpcDecisionComponent>().Has(human));
        Assert.IsGreaterThan(decisionsBefore, decisions.Decisions);
        Assert.AreEqual(CreaturePosition, session.PositionOf(session.PlayerId));
        Assert.IsFalse(session.UsedBy(session.PlayerId).Any());
        Assert.AreEqual(NpcDecisionGateKind.Player, session.DecisionView.GateOf(session.PlayerId).Kind);
    }

    [TestMethod]
    public void DecisionView_TracesOnlyTheInspectedEntity()
    {
        var session = BuildSession();
        var inspected = session.Spawn(Goblin.Id, CreaturePosition);
        var other = session.Spawn(Goblin.Id, new Vector3Int(20, 20, GroundLayer));
        var view = session.DecisionView;
        view.Inspect(inspected);

        session.RunFrames(3 * FramesPerSecond);

        Assert.IsGreaterThanOrEqualTo(0, view.LastDecisionFrame(inspected));
        Assert.IsNotEmpty(view.LastTrace.Entries);
        Assert.IsFalse(view.SetOf(inspected).IsEmpty);
        Assert.AreEqual(-1, view.LastDecisionFrame(other));
        Assert.IsNull(view.LastChoice(other));
    }

    [TestMethod]
    public void DecisionView_ChestHasNoBehaviors()
    {
        var session = BuildSession();
        var chest = session.Spawn(Game.Blueprints.Objects.TreasureChest.Id, CreaturePosition);

        Assert.IsFalse(session.DecisionView.Decides(chest));
        Assert.IsTrue(session.DecisionView.SetOf(chest).IsEmpty);
    }

    [TestMethod]
    public void ApplyingAClass_RewritesTheSetIdOnce()
    {
        var session = BuildSession();
        var goblin = session.Spawn(Goblin.Id, CreaturePosition);
        var decisions = session.Components.GetPackedPool<NpcDecisionComponent>();
        var spawnSetId = decisions.GetReadonly(goblin).BehaviorSetId;
        var engineerId = session.Build.Context.Definitions.GetId(Engineer.Id);

        session.Build.Factory.Apply(goblin, engineerId);
        var appliedSetId = decisions.GetReadonly(goblin).BehaviorSetId;
        session.Build.Factory.Apply(goblin, engineerId);

        Assert.AreNotEqual(spawnSetId, appliedSetId);
        Assert.AreEqual(appliedSetId, decisions.GetReadonly(goblin).BehaviorSetId);
        Assert.AreEqual(session.Build.Context.Definitions.Compose(Goblin.Id, Engineer.Id), appliedSetId);
    }

    [TestMethod]
    public void BlueprintGrantingAnUnknownBehavior_FailsResolveAll()
    {
        var session = BuildSession();
        session.Build.Context.Definitions.Register(new Game.Blueprints.BlueprintDefinition(new Guid("5e1f0a00-4c0d-4b1e-9a55-0000000000f2"), "Unknown Behavior Goblin")
        {
            Includes = [Goblin.Id],
            Behaviors = [new(new Guid("5e1f0a00-4c0d-4b1e-9a55-0000000000f3"), Rank: 1)],
        });

        var failure = Assert.ThrowsExactly<InvalidOperationException>(session.Build.Context.NpcDecisions.Sets.ResolveAll);

        StringAssert.Contains(failure.Message, "Unknown Behavior Goblin");
    }

    [TestMethod]
    public void TestDummyWindingUp_IsAmongTheLocalWindupsTheMapTelegraphs()
    {
        var session = BuildSession(playerPosition: Beside(CreaturePosition));
        var dummy = session.Spawn(TestDummyBlueprint.Id, CreaturePosition);
        var stateView = new GameViews(session.Build.World, session.Build.Context, session.Build.LocalTierRoster).ActionStateView;
        var windups = new List<(int EntityId, PendingWindupComponent Pending)>();
        var telegraphed = false;

        session.RunFrames(3 * FramesPerSecond, () =>
        {
            stateView.CopyLocalPendingWindups(session.PlayerId, windups);
            telegraphed |= windups.Any(windup => windup.EntityId == dummy);
        });

        Assert.IsTrue(session.Build.LocalTierRoster.IsLocal(dummy));
        Assert.IsTrue(telegraphed);
    }
}
