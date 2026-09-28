using Engine.ECS.Entities;
using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Systems;
using Game.Modules.AbilityScores.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Blueprints;
using Game.Modules.Core;
using Game.Modules.StatModifiers;
using Game.Modules.Death;
using Game.Modules.Mana;
using Game.Modules.AbilityScores;
using Game.Modules.StatusEffectAura;
using Game.Modules.BodyPartEffects;
using Game.Modules.Race;

namespace Game.Modules.Actions;

/// <summary>
/// Parameterless (required for runtime discovery) with its runtime dependencies (ActionCatalog,
/// IMapQuery, EventBus, IPlayerQuery, StatusEffectAuraApplierRegistry) supplied via
/// IGameModule.Configure instead of the constructor. Doesn't require StatusEffectsModule:
/// GameModuleContext.StatusEffectAuraAppliers is always a live, shared registry regardless of
/// which effect modules (if any) are loaded -- ActionEffectResolver's StatusEffects grant is a
/// graceful no-op (TryGet returning false) for any StatusEffectType nothing registered an
/// applier for.
///
/// Also owns PotionCooldownComponent/PotionCooldownSystem (Game.Modules.Actions.Activators/
/// Systems) -- that bookkeeping is a property of a PotionActivator-kind activation happening, not
/// of Inventory storage/stacking, so it lives with the rest of the activation machinery here
/// rather than in InventoryModule. ScrollMasteryComponent gets the same treatment for the same
/// reason, one level up from PotionActivator specifically to ScrollActivator activations in
/// general. Scroll of Torch's own map-coloring effect, by contrast, lives entirely in
/// Game.Modules.StatusEffectAura (AuraSourceGrant/AuraSourceExpiryComponent/
/// AuraSourceExpirySystem) -- it's a StatusEffectAuraSourceComponent grant like any other, not a
/// bespoke Actions-owned component, specifically so MapWindow never needs ability-specific
/// rendering knowledge (see MapTintGrid, which already renders any aura source generically).
/// </summary>
public sealed class ActionsModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000c");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, DeathModule.ModuleId, ManaModule.ModuleId, AbilityScoresModule.ModuleId, StatusEffectAuraModule.ModuleId, BodyPartEffectsModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId, BlueprintsModule.ModuleId];

    private ActionCatalog _actionCatalog = null!;
    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private MathUtility _mathUtility = null!;
    private IPlayerQuery _playerQuery = null!;
    private FloatingTextFeed _floatingTextFeed = null!;
    private StatusEffectAuraApplierRegistry _statusEffectAppliers = null!;
    private ProcessingTierEvents _processingTierEvents = null!;
    private SimulationScope _simulationScope = null!;
    private EntityKeys _entityKeys = null!;
    private SimulationClock _simulationClock = null!;
    private BlueprintRegistry _creatures = null!;

    public void Configure(GameModuleContext context)
    {
        _actionCatalog = context.Actions;
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
        _mathUtility = context.MathUtility;
        _playerQuery = context.PlayerQuery;
        _floatingTextFeed = context.FloatingTextFeed;
        _statusEffectAppliers = context.StatusEffectAuraAppliers;
        _processingTierEvents = context.ProcessingTierEvents;
        _simulationScope = context.SimulationScope;
        _entityKeys = context.EntityKeys;
        _simulationClock = context.SimulationClock;
        _creatures = context.Definitions;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<ActionInstanceComponent>(initialCapacity: 64);

        // Sparse: written the first time an entity uses an action that has a cooldown at all.
        componentManager.RegisterMultiPool<ActionCooldownComponent>(initialCapacity: 64);
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(
            static (ref PendingDelayedActionComponent existing, PendingDelayedActionComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<DodgingComponent>(
            static (ref DodgingComponent existing, DodgingComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingActionActivationComponent>(
            static (ref PendingActionActivationComponent existing, PendingActionActivationComponent incoming) => existing = incoming);
        // Player-only, 24 hotkey slots total -- dense capacity matches the slot count.
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>(initialCapacity: 24);
        // Player-only, only 4 expansions exist.
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(
            static (ref existing, incoming) => existing = incoming, initialCapacity: 4);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        // Player-only, exceedingly rare (hours between masteries) -- starts small, grows organically.
        componentManager.RegisterMultiPool<ScrollMasteryComponent>(initialCapacity: 8);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No cooldown system: an action's cooldown is a deadline (ActionInstanceComponent.
        // CooldownReadyAtFrame), read against the current frame rather than walked down.

        systemManager.Register(new PotionCooldownSystem(componentManager.GetPackedPool<PotionCooldownComponent>()));

        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        var mana = componentManager.GetPackedPool<ManaComponent>();
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();
        var auraSources = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        var hotkeyExpansionUnlocks = componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>();
        var bodyParts = EntityBodyParts.For(componentManager, _creatures);
        var meleeDisabled = componentManager.GetPackedPool<MeleeDisabledComponent>();
        var dodgingEntities = componentManager.GetPackedPool<DodgingComponent>();

        var processingTiers = new ProcessingTierQuery(componentManager.GetDirectPool<ProcessingTierComponent>());

        systemManager.Register(new DodgeExpirySystem(dodgingEntities));

        WireStagger(componentManager);

        systemManager.Register(new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            EntityActions.For(componentManager, _actionCatalog, _creatures),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _actionCatalog,
            _mapQuery,
            _eventBus,
            _mathUtility,
            _playerQuery,
            _statusEffectAppliers,
            componentManager,
            _entityKeys,
            statModifiers,
            deadEntities,
            abilityScores,
            mana,
            auraSources,
            hotkeyExpansionUnlocks,
            bodyParts,
            dodgingEntities,
            processingTiers,
            _simulationScope,
            _processingTierEvents,
            _creatures,
            _floatingTextFeed));

        systemManager.Register(new ActionActivationSystem(
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            EntityActions.For(componentManager, _actionCatalog, _creatures),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _actionCatalog,
            _mapQuery,
            _eventBus,
            _mathUtility,
            _playerQuery,
            _statusEffectAppliers,
            componentManager,
            _entityKeys,
            statModifiers,
            deadEntities,
            mana,
            abilityScores,
            auraSources,
            hotkeyExpansionUnlocks,
            bodyParts,
            meleeDisabled,
            dodgingEntities,
            processingTiers,
            _creatures,
            _floatingTextFeed));
    }

    /// <summary>A staggered entity loses its windup, and with it the time the windup already cost: the lock it set is kept.</summary>
    private void WireStagger(ComponentManager componentManager)
    {
        var pendingDelayedActions = componentManager.GetPackedPool<PendingDelayedActionComponent>();
        var actionLocks = componentManager.GetPackedPool<ActionLockComponent>();

        _eventBus.Subscribe<EntityStaggeredEvent>(staggered =>
            WindupCancel.TryCancel(pendingDelayedActions, actionLocks, staggered.EntityId, _simulationClock.CurrentFrame, releaseLock: false));
    }
}
