using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.Actions.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class DelayedActionSystemTests
{
    private const int CasterEntityId = 0;
    private const int TargetEntityId = 2;
    private const uint ReadyAtFrame = 30;
    private static readonly Guid ActionId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Vector3Int TargetTile = new(5, 5, 0);

    /// <summary>Never rolls a crit -- NextDouble always returns 1.0, comfortably above any crit chance -- so damage-amount assertions here stay deterministic.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<(int, int, int), int> _occupantByPosition = [];

        public Vector3Int MapSize { get; } = new(100, 100, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => true;
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;

        public void SetOccupant(Vector3Int position, int entityId) => _occupantByPosition[(position.X, position.Y, position.Z)] = entityId;

        public int GetEntityIdAt(Vector3Int position) =>
            _occupantByPosition.TryGetValue((position.X, position.Y, position.Z), out var id) ? id : -1;

        public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) =>
            GetEntityIdAt(position) is var entityId && entityId != -1 ? [entityId] : [];

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }
    }

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    /// <summary>
    /// No ProcessingTier pool anywhere in this fixture, deliberately: a windup now resolves on its
    /// own frame at every tier, so there is no cadence left for a tier to change.
    /// </summary>
    private static (DelayedActionSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, ActionCatalog ActionCatalog) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            ActionId, "Test Delayed Attack", null, "#", default, [],
            Effects: [new ActionEffect([new DirectDamage(MinFlatDamage: 0, MaxFlatDamage: 0)])],
            Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 30, CooldownFrames: null))));

        var system = new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            actionCatalog,
            mapQuery,
            new EventBus(),
            new MathUtility(),
            playerQuery: null,
            new StatusEffectAuraApplierRegistry(),
            componentManager,
            statModifiers: null,
            componentManager.GetPackedPool<DeadComponent>());

        return (system, componentManager, mapQuery, actionCatalog);
    }

    private static float HealthOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<SimpleHealthComponent>().TryGetReadonly(entityId, out var health) ? health.CurrentHealth : -1f;

    private static bool HasPending(ComponentManager componentManager) =>
        componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId);

    /// <summary>Builds an ActionInstanceComponent whose Override pins the catalog action's shared DirectDamage entry to a fixed flat value, mirroring how a real per-race grant (see ActionOverrideEffects) makes damage deterministic instead of rolling MinFlatDamage..MaxFlatDamage.</summary>
    private static ActionInstanceComponent FixedDamageInstance(ActionCatalog actionCatalog, Guid actionId, ushort damageAmount)
    {
        actionCatalog.TryGet(actionId, out var baseAction);
        return new ActionInstanceComponent(actionId, ActionOverrideEffects.OverrideFlatDamage(baseAction!, damageAmount));
    }

    /// <summary>Sets up a caster mid-windup, its effect due on ReadyAtFrame.</summary>
    private static void ArmWindup(ComponentManager componentManager, FakeMapQuery mapQuery, ActionCatalog actionCatalog)
    {
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, FixedDamageInstance(actionCatalog, ActionId, 15));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile], ReadyAtFrame));
    }

    private static void Run(DelayedActionSystem system, long from, long to)
    {
        for (var frame = from; frame <= to; frame++)
        {
            system.Update(Frame(frame), 0);
        }
    }

    [TestMethod]
    public void WindupStillRunning_EffectIsNotResolved()
    {
        var (system, componentManager, mapQuery, actionCatalog) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        Run(system, 0, ReadyAtFrame - 1);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
        Assert.IsTrue(HasPending(componentManager), "Still mid-windup -- the pending action must not be cleared yet.");
    }

    [TestMethod]
    public void OnItsReadyFrame_ResolvesEffectAndClearsPending()
    {
        var (system, componentManager, mapQuery, actionCatalog) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        Run(system, 0, ReadyAtFrame);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(HasPending(componentManager), "Resolved -- the pending action must be cleared so it isn't resolved again.");
    }

    /// <summary>The windup fires once, not once per frame after its deadline.</summary>
    [TestMethod]
    public void AfterResolving_FurtherFramesDoNotResolveAgain()
    {
        var (system, componentManager, mapQuery, actionCatalog) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        Run(system, 0, ReadyAtFrame + 120);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, HealthOf(componentManager, TargetEntityId));
    }

    [TestMethod]
    public void CasterIsDead_DoesNotResolveEffectButStillClearsPending()
    {
        var (system, componentManager, mapQuery, actionCatalog) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);
        componentManager.GetPackedPool<DeadComponent>().Add(CasterEntityId, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));

        Run(system, 0, ReadyAtFrame);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "A corpse can't finish a windup.");
        Assert.IsFalse(HasPending(componentManager), "Must still be cleared on death, not just skipped -- nothing else ever removes it once dead.");
    }

    /// <summary>Cancellation (right-click tap / Escape) just removes the component; the wheel entry left behind must be dropped as stale rather than firing into nothing.</summary>
    [TestMethod]
    public void CancelledBeforeItsReadyFrame_NeverResolves()
    {
        var (system, componentManager, mapQuery, actionCatalog) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        Run(system, 0, 10);
        componentManager.GetPackedPool<PendingDelayedActionComponent>().Remove(CasterEntityId);

        Run(system, 11, ReadyAtFrame + 60);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(HasPending(componentManager));
    }

    /// <summary>Re-queuing over a still-running windup (the Merge that ActionActivationSystem does) follows the new deadline, and the superseded one must not resolve it early.</summary>
    [TestMethod]
    public void ReQueuedWithALaterDeadline_ResolvesOnTheNewFrameOnly()
    {
        var (system, componentManager, mapQuery, actionCatalog) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        Run(system, 0, 10);
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile], readyAtFrame: 90));

        Run(system, 11, ReadyAtFrame);
        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "The old frame passed, but this windup now ends later.");

        Run(system, ReadyAtFrame + 1, 90);
        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, HealthOf(componentManager, TargetEntityId));
    }

    [TestMethod]
    public void NoPendingAction_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));

        Run(system, 0, 120);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
    }
}
