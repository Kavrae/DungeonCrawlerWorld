using Engine.ECS.Entities;
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
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Blueprints;

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

        public MapBounds Bounds { get; } = new(0, 0, 100, 100, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => true;

        public void SetOccupant(Vector3Int position, int entityId) => _occupantByPosition[(position.X, position.Y, position.Z)] = entityId;

        public int GetEntityIdAt(Vector3Int position) =>
            _occupantByPosition.TryGetValue((position.X, position.Y, position.Z), out var id) ? id : -1;

        public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) =>
            GetEntityIdAt(position) is var entityId && entityId != -1 ? [entityId] : [];

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }
    }

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    /// <summary>
    /// The fixture has no tier pool driving a cadence -- a windup resolves on its own frame at every
    /// tier -- but it does wire ProcessingTierEvents, which is how freezing cancels a windup.
    /// </summary>
    private static (DelayedActionSystem System, ComponentManager ComponentManager, FakeMapQuery MapQuery, ActionCatalog ActionCatalog, ProcessingTierEvents TierEvents) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterMultiPool<ActionCooldownComponent>();
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DeadComponent>(static (ref existing, incoming) => existing = incoming);

        var mapQuery = new FakeMapQuery();

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            ActionId, "Test Delayed Attack", null, "#", default, [],
            Effects: [new ActionEffect([new DirectDamage(MinFlatDamage: 0, MaxFlatDamage: 0)])],
            Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 10), new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 30, CooldownFrames: null))));

        var tierEvents = new ProcessingTierEvents();
        var system = new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            EntityActions.For(componentManager, actionCatalog, new BlueprintRegistry()),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            actionCatalog,
            mapQuery,
            new EventBus(),
            new MathUtility(),
            playerQuery: null,
            new StatusEffectAuraApplierRegistry(),
            componentManager,
            new EntityKeys(),
            statModifiers: null,
            componentManager.GetPackedPool<DeadComponent>(),
            processingTierEvents: tierEvents);

        return (system, componentManager, mapQuery, actionCatalog, tierEvents);
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

    /// <summary>Tiers the caster through the real resolver, which is what raises TierChanged -- the tier follows from where the caster stands relative to the reference, so the caller names the tier it wants and this places it accordingly.</summary>
    private static void FreezeCaster(ProcessingTierEvents tierEvents, ProcessingTierLevel tier)
    {
        var tiers = new Engine.ECS.Components.Stores.DirectComponentPool<ProcessingTierComponent>(8, static (ref existing, incoming) => existing = incoming);
        var transforms = new Engine.ECS.Components.Stores.DirectComponentPool<Game.Modules.Core.Components.TransformComponent>(8, static (ref existing, incoming) => existing = incoming);
        var resolver = new ProcessingTierResolver();
        resolver.Wire(tiers, transforms, tierEvents);
        resolver.SetReferencePosition(new Vector3Int(0, 0, 0));
        tiers.Add(CasterEntityId, new ProcessingTierComponent(ProcessingTierLevel.Local));

        var position = tier switch
        {
            ProcessingTierLevel.Neighborhood => new Vector3Int(200, 0, 0),
            ProcessingTierLevel.Borough => new Vector3Int(1200, 0, 0),
            _ => new Vector3Int(5000, 0, 0),
        };

        resolver.EnsureTiered(CasterEntityId, position);
    }

    /// <summary>A windup is cancelled the moment its owner freezes: its target is either frozen too, and so untargetable across the seam, or simulated and long gone by the time the owner thaws.</summary>
    [TestMethod]
    public void OwnerFreezesMidWindup_PendingActionIsCancelled()
    {
        var (system, componentManager, mapQuery, actionCatalog, tierEvents) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        FreezeCaster(tierEvents, ProcessingTierLevel.Borough);
        Run(system, 0, ReadyAtFrame);

        Assert.IsFalse(HasPending(componentManager), "Freezing cancels the windup.");
        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
    }

    [TestMethod]
    public void OwnerRetieredToASimulatedTierMidWindup_StillResolves()
    {
        var (system, componentManager, mapQuery, actionCatalog, tierEvents) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        FreezeCaster(tierEvents, ProcessingTierLevel.Neighborhood);
        Run(system, 0, ReadyAtFrame);

        Assert.IsFalse(HasPending(componentManager));
        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, HealthOf(componentManager, TargetEntityId));
    }

    [TestMethod]
    public void WindupStillRunning_EffectIsNotResolved()
    {
        var (system, componentManager, mapQuery, actionCatalog, _) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        Run(system, 0, ReadyAtFrame - 1);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
        Assert.IsTrue(HasPending(componentManager), "Still mid-windup -- the pending action must not be cleared yet.");
    }

    [TestMethod]
    public void OnItsReadyFrame_ResolvesEffectAndClearsPending()
    {
        var (system, componentManager, mapQuery, actionCatalog, _) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        Run(system, 0, ReadyAtFrame);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, HealthOf(componentManager, TargetEntityId));
        Assert.IsFalse(HasPending(componentManager), "Resolved -- the pending action must be cleared so it isn't resolved again.");
    }

    /// <summary>The windup fires once, not once per frame after its deadline.</summary>
    [TestMethod]
    public void AfterResolving_FurtherFramesDoNotResolveAgain()
    {
        var (system, componentManager, mapQuery, actionCatalog, _) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);

        Run(system, 0, ReadyAtFrame + 120);

        DamageAssert.HealthAfterDamage(startingHealth: 100, expectedNormalDamage: 15, HealthOf(componentManager, TargetEntityId));
    }

    [TestMethod]
    public void CasterIsDead_DoesNotResolveEffectButStillClearsPending()
    {
        var (system, componentManager, mapQuery, actionCatalog, _) = Build();
        ArmWindup(componentManager, mapQuery, actionCatalog);
        componentManager.GetPackedPool<DeadComponent>().Add(CasterEntityId, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));

        Run(system, 0, ReadyAtFrame);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId), "A corpse can't finish a windup.");
        Assert.IsFalse(HasPending(componentManager), "Must still be cleared on death, not just skipped -- nothing else ever removes it once dead.");
    }

    /// <summary>Cancellation (right-click tap / Escape) just removes the component; the wheel entry left behind must be dropped as stale rather than firing into nothing.</summary>
    [TestMethod]
    public void CancelledBeforeItsReadyFrame_NeverResolves()
    {
        var (system, componentManager, mapQuery, actionCatalog, _) = Build();
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
        var (system, componentManager, mapQuery, actionCatalog, _) = Build();
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
        var (system, componentManager, mapQuery, _, _) = Build();
        mapQuery.SetOccupant(TargetTile, TargetEntityId);
        componentManager.Merge(TargetEntityId, new SimpleHealthComponent(100, 100));

        Run(system, 0, 120);

        Assert.AreEqual(100, HealthOf(componentManager, TargetEntityId));
    }
}
