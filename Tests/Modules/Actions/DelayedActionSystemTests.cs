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
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class DelayedActionSystemTests
{
    // Entity 0 lands in stripe-bucket 0 for every tier (entityId % StripeCount) -- bucket 0 is
    // always due at FrameCount 0 (0 % anything == 0), the same convention ActionLockSystemTests
    // uses for its own "immediate" tests, so system.Update(default, 0) reaches it without needing
    // to seed a ProcessingTierComponent first.
    private const int CasterEntityId = 0;
    private const int TargetEntityId = 2;
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

    private static (DelayedActionSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, EventBus EventBus, ActionCatalog ActionCatalog, DirectComponentPool<ProcessingTierComponent> ProcessingTiers) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterDirectPool<ProcessingTierComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();
        var eventBus = new EventBus();
        var mathUtility = new MathUtility();
        var processingTiers = componentManager.GetDirectPool<ProcessingTierComponent>();

        // Seeded Local before anything adds CasterEntityId to the stripe set: an entity with no
        // ProcessingTierComponent resolves to Beyond (see ProcessingTierWiring), whose
        // framesPerVisit is now large enough to change what a single Update does. Tests wanting
        // another tier Merge over this rather than Add, since Add throws on a duplicate.
        processingTiers.Add(CasterEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

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
            processingTiers,
            new ProcessingTierEvents(),
            statModifiers: null,
            componentManager.GetPackedPool<DeadComponent>());

        return (system, componentManager, mapQuery, eventBus, actionCatalog, processingTiers);
    }

    private static float HealthOf(ComponentManager componentManager, int entityId) =>
        componentManager.GetPackedPool<SimpleHealthComponent>().TryGetReadonly(entityId, out var health) ? health.CurrentHealth : -1f;

    /// <summary>Builds an ActionInstanceComponent whose Override pins the catalog action's shared DirectDamage entry to a fixed flat value, mirroring how a real per-race grant (see ActionOverrideEffects) makes damage deterministic instead of rolling MinFlatDamage..MaxFlatDamage.</summary>
    private static ActionInstanceComponent FixedDamageInstance(ActionCatalog actionCatalog, Guid actionId, ushort damageAmount, ushort cooldownFramesRemaining = 0)
    {
        actionCatalog.TryGet(actionId, out var baseAction);
        return new ActionInstanceComponent(actionId, ActionOverrideEffects.OverrideFlatDamage(baseAction!, damageAmount), cooldownFramesRemaining);
    }

    [TestMethod]
    public void LockStillCounting_EffectIsNotResolved()
    {
        var (system, componentManager, mapQuery, _, actionCatalog, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, FixedDamageInstance(actionCatalog, ActionId, 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 10));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile]));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
        Assert.IsTrue(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Still mid-windup -- the pending action must not be cleared yet.");
    }

    [TestMethod]
    public void LockReachesZero_ResolvesEffectAndClearsPending()
    {
        var (system, componentManager, mapQuery, _, actionCatalog, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, FixedDamageInstance(actionCatalog, ActionId, 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile]));

        system.Update(default, 0);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId), "Resolved -- the pending action must be cleared so it isn't resolved again next visit.");
    }

    [TestMethod]
    public void LockReachesZero_CasterIsDead_DoesNotResolveEffect()
    {
        var (system, componentManager, mapQuery, _, actionCatalog, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        componentManager.Merge(CasterEntityId, FixedDamageInstance(actionCatalog, ActionId, 15, cooldownFramesRemaining: 0));
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
        var (system, componentManager, mapQuery, _, actionCatalog, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));

        system.Update(default, 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
    }

    /// <summary>
    /// A Neighborhood-tiered entity (StripeCount * the Neighborhood divisor) lands in bucket
    /// entityId % 20 -- for CasterEntityId (0), that's bucket 0, due only when
    /// FrameCount % 20 == 0. The tier must be seeded before PendingDelayedActionComponent is
    /// merged, since TieredEntityStripeSet reads an entity's current tier at membership-add time
    /// (the pool's own EntityAdded event, fired by that Merge call) -- same requirement
    /// ActionLockSystemTests' own identical-shaped tests document.
    /// </summary>
    [TestMethod]
    public void LockReachesZero_ThrottledEntity_OffCycle_DoesNotResolveYet()
    {
        var (system, componentManager, mapQuery, _, actionCatalog, processingTiers) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        processingTiers.Merge(CasterEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        componentManager.Merge(CasterEntityId, FixedDamageInstance(actionCatalog, ActionId, 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile]));

        system.Update(new EngineTime(default, default, false, FrameCount: 1), 0);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "Off this entity's own tiered cycle -- not visited yet, even though its lock already reached 0.");
        Assert.IsTrue(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId));
    }

    [TestMethod]
    public void LockReachesZero_ThrottledEntity_OnEligibleCycle_ResolvesEffect()
    {
        var (system, componentManager, mapQuery, _, actionCatalog, processingTiers) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));
        processingTiers.Merge(CasterEntityId, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));
        componentManager.Merge(CasterEntityId, FixedDamageInstance(actionCatalog, ActionId, 15, cooldownFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 0));
        componentManager.Merge(CasterEntityId, new PendingDelayedActionComponent(ActionId, [TargetTile]));

        // FrameCount 0: CasterEntityId is 0, so it lands in bucket 0 of whatever tier bucket it
        // is in, and bucket 0 is due whenever FrameCount is a multiple of that bucket's stripe
        // count -- true at 0 for any divisor. A hardcoded nonzero frame here only worked while
        // the Neighborhood divisor happened to be 2.
        system.Update(new EngineTime(default, default, false, FrameCount: 0), 0);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(componentManager.GetPackedPool<PendingDelayedActionComponent>().Has(CasterEntityId));
    }
}
