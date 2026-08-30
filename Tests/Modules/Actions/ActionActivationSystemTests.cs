using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.Actions.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class ActionActivationSystemTests
{
    private const int CasterEntityId = 1;
    private const int TargetEntityId = 2;
    private static readonly Guid ImmediateActionId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DelayedActionId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FreeCastActionId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ImmediateWithCooldownActionId = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DelayedWithCooldownActionId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ImmediateWithManaCostActionId = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid FreeCastWithManaCostActionId = new("77777777-7777-7777-7777-777777777777");
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

        public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) =>
            GetEntityIdAt(position) is var entityId && entityId != -1 ? [entityId] : [];

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }
    }

    /// <summary>Never rolls a crit -- NextDouble always returns 1.0, comfortably above any crit chance -- so damage-amount assertions here stay deterministic.</summary>
    private sealed class NeverCritRandom : Random
    {
        public override double NextDouble() => 1.0;
    }

    private static (ActionActivationSystem System, ComponentManager ComponentManager, ActionCatalog Catalog, FakeMapQuery MapQuery) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingActionActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();
        var mathUtility = new MathUtility(new NeverCritRandom());
        var damageEffects = new ActionEffect[] { new([new DirectDamage(MinAmount: 0, MaxAmount: 0)]) };
        var targeting = new TargetingSpec(TargetShape.SingleTarget, Range: 10);

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            ImmediateActionId, "Test Immediate Attack", null, "#", default, [], damageEffects,
            new SpellActivator(targeting, new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));
        actionCatalog.Register(new ActionDefinition(
            DelayedActionId, "Test Delayed Attack", null, "#", default, [], damageEffects,
            new SpellActivator(targeting, new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 30, CooldownFrames: null))));
        actionCatalog.Register(new ActionDefinition(
            FreeCastActionId, "Test FreeCast Bolt", null, "#", default, [], damageEffects,
            new SpellActivator(targeting, new ActionTiming(ActionTimingCategory.FreeCast, ActionLockFrames: 0, CooldownFrames: 40))));
        actionCatalog.Register(new ActionDefinition(
            ImmediateWithCooldownActionId, "Test Immediate Attack With Cooldown", null, "#", default, [], damageEffects,
            new SpellActivator(targeting, new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 10, CooldownFrames: 200))));
        actionCatalog.Register(new ActionDefinition(
            DelayedWithCooldownActionId, "Test Delayed Attack With Cooldown", null, "#", default, [], damageEffects,
            new SpellActivator(targeting, new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 30, CooldownFrames: 150))));
        actionCatalog.Register(new ActionDefinition(
            ImmediateWithManaCostActionId, "Test Immediate Spell", null, "#", default, [], damageEffects,
            new SpellActivator(targeting, new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null), ManaCost: 5)));
        actionCatalog.Register(new ActionDefinition(
            FreeCastWithManaCostActionId, "Test FreeCast Spell", null, "#", default, [], damageEffects,
            new SpellActivator(targeting, new ActionTiming(ActionTimingCategory.FreeCast, ActionLockFrames: 0, CooldownFrames: null), ManaCost: 5)));

        var system = new ActionActivationSystem(
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            actionCatalog,
            mapQuery,
            eventBus,
            mathUtility,
            playerQuery: null,
            new StatusEffectAuraApplierRegistry(),
            componentManager,
            statModifiers: null,
            componentManager.GetPackedPool<DeadComponent>(),
            componentManager.GetPackedPool<ManaComponent>());

        return (system, componentManager, actionCatalog, mapQuery);
    }

    private static float ManaOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<ManaComponent>().TryGetReadonly(entityId, out var mana) ? mana.CurrentMana : -1f;

    private static float HealthOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<SimpleHealthComponent>().TryGetReadonly(entityId, out var health) ? health.CurrentHealth : -1f;

    private static ushort? CooldownOf(ComponentManager componentManager, int entityId, Guid actionId)
    {
        var instances = componentManager.GetMultiPool<ActionInstanceComponent>();
        for (var i = instances.GetFirstDenseIndex(entityId); i != -1; i = instances.GetNextDenseIndex(i))
        {
            var instance = instances.GetReadonlyByDenseIndex(i);
            if (instance.ActionId == actionId)
            {
                return instance.CooldownFramesRemaining;
            }
        }

        return null;
    }

    [TestMethod]
    public void Immediate_NotBlocked_AppliesDamageAndLocksAndConsumesRequest()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(85, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining);
        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void Immediate_CasterIsDead_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateActionId, [TargetTile]));
        componentManager.GetPackedPool<DeadComponent>().Add(CasterEntityId, new DeadComponent(KilledByEntityId: null, DiedAtFrame: 0));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "A corpse can't act.");
        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(CasterEntityId), "Must still be cleared on death, not just skipped -- otherwise the entity stays in this system's stripe set (and carries the stale pending request) forever, since nothing else ever removes it once dead.");
    }

    [TestMethod]
    public void Immediate_ActionLockAlreadyBlocked_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 10));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "Blocked -- no damage should be applied.");
        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(CasterEntityId), "Still dropped -- a blocked activation is a one-shot failure, not something retried next frame.");
    }

    [TestMethod]
    public void Immediate_ActionLockClear_ButOwnCooldownStillActive_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateWithCooldownActionId, damageAmount: 15, cooldownFramesRemaining: 50));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateWithCooldownActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "The shared ActionLock is clear, but the action's own longer cooldown must still gate it.");
        Assert.AreEqual((ushort?)50, CooldownOf(componentManager, CasterEntityId, ImmediateWithCooldownActionId), "A rejected activation must not restart or otherwise touch the existing cooldown.");
        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void Immediate_Fires_StartsBothTheSharedActionLockAndItsOwnLongerCooldown()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateWithCooldownActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateWithCooldownActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(85, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(10, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining, "The short shared ActionLock.");
        Assert.AreEqual((ushort?)200, CooldownOf(componentManager, CasterEntityId, ImmediateWithCooldownActionId), "The action's own, much longer cooldown -- outlives the shared lock.");
    }

    [TestMethod]
    public void Delayed_NotBlocked_LocksImmediately_ButDefersEffectToDelayedActionSystem()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(DelayedActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(DelayedActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "Delayed -- effect must not fire yet.");
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining, "The windup lock is set immediately, not deferred.");
        Assert.IsTrue(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Handed off to DelayedActionSystem via PendingDelayedActionComponent.");
    }

    [TestMethod]
    public void Delayed_Activates_StartsItsOwnCooldownAlongsideTheSharedActionLock()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(DelayedWithCooldownActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(DelayedWithCooldownActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "Delayed -- effect must not fire yet.");
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining);
        Assert.AreEqual((ushort?)150, CooldownOf(componentManager, CasterEntityId, DelayedWithCooldownActionId), "The cooldown starts at activation, the same moment as the windup lock -- not deferred to when the effect eventually resolves.");
    }

    [TestMethod]
    public void Delayed_OwnCooldownStillActive_DoesNothingEvenWithActionLockClear()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(DelayedWithCooldownActionId, damageAmount: 15, cooldownFramesRemaining: 60));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(DelayedWithCooldownActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Gated by its own cooldown before ever setting a windup.");
        Assert.AreEqual(0, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining, "Must not set the shared lock for a rejected activation.");
    }

    [TestMethod]
    public void FreeCast_OffCooldown_AppliesDamageAndStartsCooldown_WithoutTouchingSharedLock()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(FreeCastActionId, damageAmount: 20, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 30));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(FreeCastActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(80, HealthOf(componentManager, TargetEntityId), "FreeCast must fire even though the shared ActionLock is still counting down.");
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining, "FreeCast must not touch the shared lock at all.");
        Assert.AreEqual((ushort?)40, CooldownOf(componentManager, CasterEntityId, FreeCastActionId));
    }

    [TestMethod]
    public void FreeCast_StillOnCooldown_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(FreeCastActionId, damageAmount: 20, cooldownFramesRemaining: 5));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(FreeCastActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual((ushort?)5, CooldownOf(componentManager, CasterEntityId, FreeCastActionId), "Cooldown must be left untouched, not restarted, by a rejected activation.");
    }

    [TestMethod]
    public void Immediate_InsufficientMana_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ManaComponent(currentMana: 4, maximumMana: 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateWithManaCostActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateWithManaCostActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "1 mana short of the cost -- blocked, no effect.");
        Assert.AreEqual(4, ManaOf(componentManager, CasterEntityId), "A blocked activation must not spend mana.");
        Assert.AreEqual(0, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining, "A blocked activation must not set the lock either.");
    }

    [TestMethod]
    public void Immediate_SufficientMana_AppliesDamageAndSpendsMana()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ManaComponent(currentMana: 5, maximumMana: 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateWithManaCostActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateWithManaCostActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(85, HealthOf(componentManager, TargetEntityId));
        Assert.AreEqual(0, ManaOf(componentManager, CasterEntityId), "Exactly enough -- spent down to 0.");
        Assert.AreEqual(30, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining);
    }

    [TestMethod]
    public void Immediate_NoManaComponentAtAll_ManaCostAction_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateWithManaCostActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateWithManaCostActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "An entity that never gained a ManaComponent can't afford any ManaCost > 0 action -- this is what makes an action the entity can never cast possible by design.");
    }

    [TestMethod]
    public void Immediate_ZeroManaCostAction_IgnoresManaEntirely()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(ImmediateActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(ImmediateActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(85, HealthOf(componentManager, TargetEntityId), "ManaCost 0 (the default) -- no ManaComponent needed at all, same as Punch.");
    }

    [TestMethod]
    public void FreeCast_InsufficientMana_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ManaComponent(currentMana: 4, maximumMana: 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(FreeCastWithManaCostActionId, damageAmount: 20, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(FreeCastWithManaCostActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "FreeCast bypasses the shared lock but not a mana cost it can't afford.");
        Assert.AreEqual(4, ManaOf(componentManager, CasterEntityId));
    }

    [TestMethod]
    public void UnknownActionId_DoesNothing_AndConsumesRequest()
    {
        var (system, componentManager, _, _) = Build();
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(Guid.NewGuid(), [TargetTile]));

        system.Update(default, 0);

        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void NoPendingActivation_DoesNothing()
    {
        var (system, componentManager, _, mapQuery) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
    }

    /// <summary>BodyPartEffectsSystem's own hard block (every Arm/Hand simultaneously disabled) -- a Tag.Melee action must be refused outright, distinct from every other gate above which all use the shared ActionLock/cooldown/mana machinery.</summary>
    [TestMethod]
    public void Immediate_MeleeTaggedAction_MeleeDisabled_DoesNothingButStillConsumesRequest()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingActionActivationComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<Game.Modules.BodyPartEffects.Components.MeleeDisabledComponent>(static (ref existing, incoming) => { });

        var mapQuery = new FakeMapQuery();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        var mathUtility = new MathUtility(new NeverCritRandom());
        var meleeActionId = new Guid("88888888-8888-8888-8888-888888888888");
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            meleeActionId, "Test Punch", null, "#", default, [Tag.Melee, Tag.Attack],
            [new ActionEffect([new DirectDamage(MinAmount: 0, MaxAmount: 0)])],
            new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));

        var meleeDisabled = componentManager.GetPackedPool<Game.Modules.BodyPartEffects.Components.MeleeDisabledComponent>();
        meleeDisabled.Add(CasterEntityId, default);

        var system = new ActionActivationSystem(
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            actionCatalog,
            mapQuery,
            new EventBus(),
            mathUtility,
            playerQuery: null,
            new StatusEffectAuraApplierRegistry(),
            componentManager,
            meleeDisabled: meleeDisabled);

        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, new ActionInstanceComponent(meleeActionId, damageAmount: 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingActionActivationComponent(meleeActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "Every Arm/Hand disabled -- the swing must not happen at all.");
        Assert.AreEqual(0, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(CasterEntityId).CurrentLockFramesRemaining, "A refused activation must not lock the caster either.");
        Assert.IsFalse(componentManager.GetPackedPool<PendingActionActivationComponent>().Has(CasterEntityId));
    }
}
