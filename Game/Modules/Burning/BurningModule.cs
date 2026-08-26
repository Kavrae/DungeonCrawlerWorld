using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Burning.Components;
using Game.Modules.Burning.Systems;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Burning;

/// <summary>
/// Burning-specific: its own timer component and system, depending on StatusEffectsModule
/// (shared stack storage) and HealthModule (what it damages). Parameterless, with runtime
/// dependencies (EventBus, IPlayerQuery) supplied via IGameModule.Configure. Also registers a
/// TimerBasedAuraApplier&lt;BurningTimerComponent&gt; into the shared
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
        context.StatusEffectAuraAppliers.Register(new TimerBasedAuraApplier<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.ApplyStack));
        context.StatusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph,
            burning => burning.FramesUntilNextTick + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });

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
    }
}
