using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Burning;

/// <summary>
/// Burning-specific: its own entity-scoped and body-part-scoped timer components and systems,
/// depending on StatusEffectsModule (shared stack storage) and HealthModule (what it damages).
/// Parameterless, with runtime dependencies (EventBus, IPlayerQuery) supplied via
/// IGameModule.Configure. Also registers a BurningAuraApplier (dispatches entity-scoped vs
/// body-part-scoped per grant -- see its own doc comment) into the shared
/// StatusEffectAuraApplierRegistry during Configure, so StatusEffectAuraSystem can grant
/// Burning stacks without depending on this module directly.
/// </summary>
public sealed class BurningModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000008");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(StatusEffectsModule)];

    private EventBus _eventBus = null!;
    private IPlayerQuery? _playerQuery;
    private ProcessingTierEvents _processingTierEvents = null!;
    private MathUtility _mathUtility = null!;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
        _processingTierEvents = context.ProcessingTierEvents;
        _mathUtility = context.MathUtility;
        context.StatusEffectAuraAppliers.Register(new BurningAuraApplier(_mathUtility, _eventBus, _playerQuery));
        context.StatusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph,
            burning => burning.FramesUntilNextTick + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterMultiPool<BodyPartBurningTimerComponent>();
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<SimpleHealthComponent>())
        {
            return;
        }

        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var bodyParts = componentManager.IsRegistered<BodyPartComponent>()
            ? componentManager.GetMultiPool<BodyPartComponent>()
            : null;
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;

        systemManager.Register(new BurningSystem(
            componentManager.GetPackedPool<BurningTimerComponent>(),
            componentManager.GetMultiPool<StatusEffectStack>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _eventBus,
            _playerQuery,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            _mathUtility,
            statModifiers,
            bodyParts));

        // Always registered alongside BurningSystem, even before any Complex entity exists --
        // mirrors HealthModule's own "each empty and free until populated" precedent for
        // SimpleHealthRegenSystem/ComplexHealthRegenSystem. Guarded by the same SimpleHealthComponent
        // check above (BodyPartComponent is registered in that same HealthModule.RegisterComponents
        // call, so it's always safely fetchable here too).
        systemManager.Register(new BodyPartBurningSystem(
            componentManager.GetMultiPool<BodyPartBurningTimerComponent>(),
            componentManager.GetMultiPool<BodyPartStatusEffectStack>(),
            componentManager.GetMultiPool<BodyPartComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _eventBus,
            _playerQuery,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            statModifiers,
            deadEntities));
    }
}
