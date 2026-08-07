using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Abilities.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Abilities;

[TestClass]
public sealed class DelayedActionSystemTests
{
    private const int CasterEntityId = 1;
    private const int TargetEntityId = 2;
    private static readonly Guid AbilityId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Vector3Int TargetTile = new(5, 5, 0);

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

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }
    }

    private static (DelayedActionSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, EventBus EventBus) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<AbilityInstanceComponent>();
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();

        var abilityCatalog = new AbilityCatalog();
        abilityCatalog.Register(new AbilityDefinition(
            AbilityId,
            "Test Delayed Attack",
            "#",
            new TargetingSpec(TargetShape.SingleTarget, Range: 10),
            new AbilityTiming(ActionTimingCategory.Delayed, ActionLockFrames: 30, CooldownFrames: null),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [])));

        var system = new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<AbilityInstanceComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            abilityCatalog,
            mapQuery,
            eventBus,
            playerQuery: null,
            new StatusEffectAuraApplierRegistry(),
            componentManager,
            statModifiers: null,
            componentManager.GetPackedPool<DeadComponent>());

        return (system, componentManager, mapQuery, eventBus);
    }

    private static float HealthOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<HealthComponent>().TryGetReadonly(entityId, out var health) ? health.CurrentHealth : -1f;

    [TestMethod]
    public void LockStillCounting_EffectIsNotResolved()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(AbilityId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 30, lockFramesRemaining: 10));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(AbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
        Assert.IsTrue(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Still mid-windup -- the pending action must not be cleared yet.");
    }

    [TestMethod]
    public void LockReachesZero_ResolvesEffectAndClearsPending()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(AbilityId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 30, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(AbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(85, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Resolved -- the pending action must be cleared so it isn't resolved again next visit.");
    }

    [TestMethod]
    public void LockReachesZero_CasterIsDead_DoesNotResolveEffect()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(AbilityId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 30, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(AbilityId, [TargetTile]));
        componentManager.GetPackedPool<DeadComponent>().Add(CasterEntityId, new DeadComponent(KilledByEntityId: null));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "A corpse can't finish a windup.");
    }

    [TestMethod]
    public void NoPendingAction_DoesNothing()
    {
        var (system, componentManager, mapQuery, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 100));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
    }
}
