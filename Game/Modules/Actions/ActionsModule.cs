using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Systems;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Actions;

/// <summary>
/// Parameterless (required for runtime discovery) with its runtime dependencies (ActionCatalog,
/// IMapQuery, EventBus, IPlayerQuery, StatusEffectAuraApplierRegistry) supplied via
/// IGameModule.Configure instead of the constructor. No hard Dependencies on StatusEffectsModule:
/// GameModuleContext.StatusEffectAuraAppliers is always a live, shared registry regardless of
/// which effect modules (if any) are loaded -- ActionEffectResolver's StatusEffects grant is a
/// graceful no-op (TryGet returning false) for any StatusEffectType nothing registered an
/// applier for, the same optional treatment StatModifierComponent/SimpleHealthComponent already get.
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
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000c");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private ActionCatalog _actionCatalog = null!;
    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private MathUtility _mathUtility = null!;
    private IPlayerQuery? _playerQuery;
    private StatusEffectAuraApplierRegistry _statusEffectAppliers = null!;
    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context)
    {
        _actionCatalog = context.Actions;
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
        _mathUtility = context.MathUtility;
        _playerQuery = context.PlayerQuery;
        _statusEffectAppliers = context.StatusEffectAuraAppliers;
        _processingTierEvents = context.ProcessingTierEvents;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(
            static (ref PendingDelayedActionComponent existing, PendingDelayedActionComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingActionActivationComponent>(
            static (ref PendingActionActivationComponent existing, PendingActionActivationComponent incoming) => existing = incoming);
        // Player-only, 24 hotkey slots total -- small entity-index seed, dense capacity matches the slot count.
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>(maximumEntityCount: 2, initialCapacity: 24);
        // Player-only, only 4 expansions exist.
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(
            static (ref existing, incoming) => existing = incoming, maximumEntityCount: 2, initialCapacity: 4);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        // Player-only, exceedingly rare (hours between masteries) -- small seed, grows organically.
        componentManager.RegisterMultiPool<ScrollMasteryComponent>(maximumEntityCount: 2, initialCapacity: 8);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new ActionCooldownSystem(
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents));

        systemManager.Register(new PotionCooldownSystem(componentManager.GetPackedPool<PotionCooldownComponent>()));

        if (!componentManager.IsRegistered<SimpleHealthComponent>())
        {
            return;
        }

        var statModifiers = componentManager.GetOptionalMultiPool<StatModifierComponent>();
        var deadEntities = componentManager.GetOptionalPackedPool<DeadComponent>();
        var mana = componentManager.GetOptionalPackedPool<ManaComponent>();
        var abilityScores = componentManager.GetOptionalMultiPool<AbilityScoreComponent>();
        var auraSources = componentManager.GetOptionalMultiPool<StatusEffectAuraSourceComponent>();
        var hotkeyExpansionUnlocks = componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>();
        var bodyParts = componentManager.GetOptionalMultiPool<BodyPartComponent>();

        systemManager.Register(new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _actionCatalog,
            _mapQuery,
            _eventBus,
            _mathUtility,
            _playerQuery,
            _statusEffectAppliers,
            componentManager,
            statModifiers,
            deadEntities,
            abilityScores,
            auraSources,
            hotkeyExpansionUnlocks,
            bodyParts));

        systemManager.Register(new ActionActivationSystem(
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _actionCatalog,
            _mapQuery,
            _eventBus,
            _mathUtility,
            _playerQuery,
            _statusEffectAppliers,
            componentManager,
            statModifiers,
            deadEntities,
            mana,
            abilityScores,
            auraSources,
            hotkeyExpansionUnlocks,
            bodyParts));
    }
}
