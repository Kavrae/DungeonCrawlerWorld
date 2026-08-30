using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.Actions.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class DelayedActionSystemTests
{
    private const int CasterEntityId = 1;
    private const int TargetEntityId = 2;
    private static readonly Guid ActionId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Vector3Int TargetTile = new(5, 5, 0);

    /// <summary>Never rolls a crit -- NextDouble always returns 1.0, comfortably above any crit chance -- so damage-amount assertions here stay deterministic.</summary>
    private sealed class NeverCritRandom : Random
    {
        public override double NextDouble() => 1.0;
    }

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

    private static (DelayedActionSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, EventBus EventBus) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();
        var mathUtility = new MathUtility(new NeverCritRandom());

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            ActionId, "Test Delayed Attack", null, "#", default, [],
            Effects: [new ActionEffect([new DirectDamage(MinFlatDamage: 0, MaxFlatDamage: 0)])],
            Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 30, CooldownFrames: null))));

        var system = new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            actionCatalog,
            mapQuery,
            eventBus,
            mathUtility,
            playerQuery: null,
            new StatusEffectAuraApplierRegistry(),
            componentManager,
            statModifiers: null,
            componentManager.GetPackedPool<DeadComponent>());

        return (system, componentManager, mapQuery, eventBus);
    }

    private static float HealthOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<SimpleHealthComponent>().TryGetReadonly(entityId, out var health) ? health.CurrentHealth : -1f;

    [TestMethod]
    public void LockStillCounting_EffectIsNotResolved()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 10));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
        Assert.IsTrue(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Still mid-windup -- the pending action must not be cleared yet.");
    }

    [TestMethod]
    public void LockReachesZero_ResolvesEffectAndClearsPending()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(85, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Resolved -- the pending action must be cleared so it isn't resolved again next visit.");
    }

    [TestMethod]
    public void LockReachesZero_CasterIsDead_DoesNotResolveEffect()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile]));
        componentManager.GetPackedPool<DeadComponent>().Add(CasterEntityId, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "A corpse can't finish a windup.");
        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Must still be cleared on death, not just skipped -- otherwise the entity stays in this system's stripe set (and carries the stale pending component) forever, since nothing else ever removes it once dead.");
    }

    [TestMethod]
    public void NoPendingAction_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
    }
}
