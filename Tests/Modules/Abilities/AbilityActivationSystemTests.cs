using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Abilities.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Abilities;

[TestClass]
public sealed class AbilityActivationSystemTests
{
    private const int CasterEntityId = 1;
    private const int TargetEntityId = 2;
    private static readonly Guid ImmediateAbilityId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DelayedAbilityId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FreeCastAbilityId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ImmediateWithCooldownAbilityId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DelayedWithCooldownAbilityId = new("55555555-5555-5555-5555-555555555555");
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

    private static (AbilityActivationSystem System, ComponentManager ComponentManager, AbilityCatalog Catalog, FakeMapQuery MapQuery) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingAbilityActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<AbilityInstanceComponent>();
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();

        var abilityCatalog = new AbilityCatalog();
        abilityCatalog.Register(new AbilityDefinition(
            ImmediateAbilityId,
            "Test Immediate Attack",
            "#",
            new AbilityTargeting(TargetShape.SingleTarget, Range: 10),
            new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [])));
        abilityCatalog.Register(new AbilityDefinition(
            DelayedAbilityId,
            "Test Delayed Attack",
            "#",
            new AbilityTargeting(TargetShape.SingleTarget, Range: 10),
            new AbilityTiming(ActionTimingCategory.Delayed, ActionLockFrames: 30, CooldownFrames: null),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [])));
        abilityCatalog.Register(new AbilityDefinition(
            FreeCastAbilityId,
            "Test FreeCast Bolt",
            "#",
            new AbilityTargeting(TargetShape.SingleTarget, Range: 10),
            new AbilityTiming(ActionTimingCategory.FreeCast, ActionLockFrames: 0, CooldownFrames: 40),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [])));
        abilityCatalog.Register(new AbilityDefinition(
            ImmediateWithCooldownAbilityId,
            "Test Immediate Attack With Cooldown",
            "#",
            new AbilityTargeting(TargetShape.SingleTarget, Range: 10),
            new AbilityTiming(ActionTimingCategory.Immediate, ActionLockFrames: 10, CooldownFrames: 200),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [])));
        abilityCatalog.Register(new AbilityDefinition(
            DelayedWithCooldownAbilityId,
            "Test Delayed Attack With Cooldown",
            "#",
            new AbilityTargeting(TargetShape.SingleTarget, Range: 10),
            new AbilityTiming(ActionTimingCategory.Delayed, ActionLockFrames: 30, CooldownFrames: 150),
            new AbilityEffect(DamageAmount: 0, StatusEffects: [])));

        var system = new AbilityActivationSystem(
            componentManager.GetPackedPool<PendingAbilityActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<AbilityInstanceComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            abilityCatalog,
            mapQuery,
            eventBus,
            playerQuery: null);

        return (system, componentManager, abilityCatalog, mapQuery);
    }

    private static short HealthOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<HealthComponent>().TryGetReadonly(entityId, out var health) ? health.CurrentHealth : (short)-1;

    private static short CooldownOf(ComponentManager componentManager, int entityId, Guid abilityId)
    {
        var instances = componentManager.GetMultiPool<AbilityInstanceComponent>();
        for (var i = instances.GetFirstDenseIndex(entityId); i != -1; i = instances.GetNextDenseIndex(i))
        {
            var instance = instances.GetReadonlyByDenseIndex(i);
            if (instance.AbilityId == abilityId)
            {
                return instance.CooldownFramesRemaining;
            }
        }

        return -1;
    }

    [TestMethod]
    public void Immediate_NotBlocked_AppliesDamageAndLocksAndConsumesRequest()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(ImmediateAbilityId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(ImmediateAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(85, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).LockFramesRemaining);
        Assert.IsFalse(componentManager.GetPackedPool<PendingAbilityActivationComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void Immediate_ActionLockAlreadyBlocked_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(ImmediateAbilityId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 30, lockFramesRemaining: 10));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(ImmediateAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "Blocked -- no damage should be applied.");
        Assert.IsFalse(componentManager.GetPackedPool<PendingAbilityActivationComponent>().Has(CasterEntityId), "Still dropped -- a blocked activation is a one-shot failure, not something retried next frame.");
    }

    [TestMethod]
    public void Immediate_ActionLockClear_ButOwnCooldownStillActive_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(ImmediateWithCooldownAbilityId, damageAmount: 15, cooldownFramesRemaining: 50));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(ImmediateWithCooldownAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "The shared ActionLock is clear, but the ability's own longer cooldown must still gate it.");
        Assert.AreEqual(50, CooldownOf(componentManager, CasterEntityId, ImmediateWithCooldownAbilityId), "A rejected activation must not restart or otherwise touch the existing cooldown.");
        Assert.IsFalse(componentManager.GetPackedPool<PendingAbilityActivationComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void Immediate_Fires_StartsBothTheSharedActionLockAndItsOwnLongerCooldown()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(ImmediateWithCooldownAbilityId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(ImmediateWithCooldownAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(85, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(10, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).LockFramesRemaining, "The short shared ActionLock.");
        Assert.AreEqual(200, CooldownOf(componentManager, CasterEntityId, ImmediateWithCooldownAbilityId), "The ability's own, much longer cooldown -- outlives the shared lock.");
    }

    [TestMethod]
    public void Delayed_NotBlocked_LocksImmediately_ButDefersEffectToDelayedActionSystem()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(DelayedAbilityId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(DelayedAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "Delayed -- effect must not fire yet.");
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).LockFramesRemaining, "The windup lock is set immediately, not deferred.");
        Assert.IsTrue(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Handed off to DelayedActionSystem via PendingDelayedActionComponent.");
    }

    [TestMethod]
    public void Delayed_Activates_StartsItsOwnCooldownAlongsideTheSharedActionLock()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(DelayedWithCooldownAbilityId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(DelayedWithCooldownAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "Delayed -- effect must not fire yet.");
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).LockFramesRemaining);
        Assert.AreEqual(150, CooldownOf(componentManager, CasterEntityId, DelayedWithCooldownAbilityId), "The cooldown starts at activation, the same moment as the windup lock -- not deferred to when the effect eventually resolves.");
    }

    [TestMethod]
    public void Delayed_OwnCooldownStillActive_DoesNothingEvenWithActionLockClear()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(DelayedWithCooldownAbilityId, damageAmount: 15, cooldownFramesRemaining: 60));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(DelayedWithCooldownAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Gated by its own cooldown before ever setting a windup.");
        Assert.AreEqual(0, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).LockFramesRemaining, "Must not set the shared lock for a rejected activation.");
    }

    [TestMethod]
    public void FreeCast_OffCooldown_AppliesDamageAndStartsCooldown_WithoutTouchingSharedLock()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(FreeCastAbilityId, damageAmount: 20, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 30, lockFramesRemaining: 30));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(FreeCastAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(80, HealthOf(componentManager, TargetEntityId), "FreeCast must fire even though the shared ActionLock is still counting down.");
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).LockFramesRemaining, "FreeCast must not touch the shared lock at all.");
        Assert.AreEqual(40, CooldownOf(componentManager, CasterEntityId, FreeCastAbilityId));
    }

    [TestMethod]
    public void FreeCast_StillOnCooldown_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));
        componentManager.Merge(CasterEntityId, new AbilityInstanceComponent(FreeCastAbilityId, damageAmount: 20, cooldownFramesRemaining: 5));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(FreeCastAbilityId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(5, CooldownOf(componentManager, CasterEntityId, FreeCastAbilityId), "Cooldown must be left untouched, not restarted, by a rejected activation.");
    }

    [TestMethod]
    public void UnknownAbilityId_DoesNothing_AndConsumesRequest()
    {
        var (system, componentManager, _, _) = Build();
        componentManager.Merge(CasterEntityId, new ActionLockComponent(totalLockFrames: 0, lockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingAbilityActivationComponent(Guid.NewGuid(), [TargetTile]));

        system.Update(default, 0);

        Assert.IsFalse(componentManager.GetPackedPool<PendingAbilityActivationComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void NoPendingActivation_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new HealthComponent(100, 0, 100));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
    }
}
