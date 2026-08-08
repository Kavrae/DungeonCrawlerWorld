using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Abilities.Components;
using Game.Modules.Abilities.Systems;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.Abilities;

/// <summary>
/// Parameterless (required for runtime discovery) with its runtime dependencies (AbilityCatalog,
/// IMapQuery, EventBus, IPlayerQuery, StatusEffectAuraApplierRegistry) supplied via
/// IGameModule.Configure instead of the constructor. No hard Dependencies on StatusEffectsModule:
/// GameModuleContext.StatusEffectAuraAppliers is always a live, shared registry regardless of
/// which effect modules (if any) are loaded -- AbilityEffectResolver's StatusEffects grant is a
/// graceful no-op (TryGet returning false) for any StatusEffectType nothing registered an
/// applier for, the same optional treatment StatModifierComponent/HealthComponent already get.
/// </summary>
public sealed class AbilitiesModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-00000000000c");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private AbilityCatalog _abilityCatalog = null!;
    private IMapQuery _mapQuery = null!;
    private EventBus _eventBus = null!;
    private IPlayerQuery? _playerQuery;
    private StatusEffectAuraApplierRegistry _statusEffectAppliers = null!;
    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context)
    {
        _abilityCatalog = context.Abilities;
        _mapQuery = context.MapQuery;
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
        _statusEffectAppliers = context.StatusEffectAuraAppliers;
        _processingTierEvents = context.ProcessingTierEvents;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterMultiPool<AbilityInstanceComponent>();
        componentManager.RegisterPackedPool<PendingDelayedActionComponent>(
            static (ref PendingDelayedActionComponent existing, PendingDelayedActionComponent incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PendingAbilityActivationComponent>(
            static (ref PendingAbilityActivationComponent existing, PendingAbilityActivationComponent incoming) => existing = incoming);
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>();
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new AbilityCooldownSystem(
            componentManager.GetMultiPool<AbilityInstanceComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents));

        if (!componentManager.IsRegistered<HealthComponent>())
        {
            return;
        }

        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        var mana = componentManager.IsRegistered<ManaComponent>()
            ? componentManager.GetPackedPool<ManaComponent>()
            : null;
        var abilityScores = componentManager.IsRegistered<AbilityScoreComponent>()
            ? componentManager.GetMultiPool<AbilityScoreComponent>()
            : null;

        systemManager.Register(new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<AbilityInstanceComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _abilityCatalog,
            _mapQuery,
            _eventBus,
            _playerQuery,
            _statusEffectAppliers,
            componentManager,
            statModifiers,
            deadEntities,
            abilityScores));

        systemManager.Register(new AbilityActivationSystem(
            componentManager.GetPackedPool<PendingAbilityActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<AbilityInstanceComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
            _abilityCatalog,
            _mapQuery,
            _eventBus,
            _playerQuery,
            _statusEffectAppliers,
            componentManager,
            statModifiers,
            deadEntities,
            mana,
            abilityScores));
    }
}
