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
/// applier for, the same optional treatment StatModifierComponent/HealthComponent already get.
///
/// Also owns PotionCooldownComponent/PotionCooldownSystem (Game.Modules.Actions.Activators/
/// Systems) -- that bookkeeping is a property of a PotionActivator-kind activation happening, not
/// of Inventory storage/stacking, so it lives with the rest of the activation machinery here
/// rather than in InventoryModule.
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
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>();
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new ActionCooldownSystem(
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents));

        systemManager.Register(new PotionCooldownSystem(componentManager.GetPackedPool<PotionCooldownComponent>()));

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
        var auraSources = componentManager.IsRegistered<StatusEffectAuraSourceComponent>()
            ? componentManager.GetMultiPool<StatusEffectAuraSourceComponent>()
            : null;
        var hotkeyExpansionUnlocks = componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>();

        systemManager.Register(new DelayedActionSystem(
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
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
            hotkeyExpansionUnlocks));

        systemManager.Register(new ActionActivationSystem(
            componentManager.GetPackedPool<PendingActionActivationComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetPackedPool<PendingDelayedActionComponent>(),
            componentManager.GetPackedPool<HealthComponent>(),
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
            hotkeyExpansionUnlocks));
    }
}
