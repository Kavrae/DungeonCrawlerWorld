using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.Modules.Poison.Components;
using Game.Modules.Poison.Systems;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Game.Modules.Poison;

/// <summary>
/// Poison-specific: its own timer component and system, depending on StatusEffectsModule
/// (shared stack storage). Parameterless, with runtime dependencies (EventBus, IPlayerQuery)
/// supplied via IGameModule.Configure. Also registers a
/// TimerBasedAuraApplier&lt;PoisonTimerComponent&gt; into the shared
/// StatusEffectAuraApplierRegistry during Configure, so StatusEffectAuraSystem can grant
/// Poison stacks without depending on this module directly.
/// </summary>
public sealed class PoisonModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000009");

    public IReadOnlyList<Type> Dependencies { get; } = [typeof(StatusEffectsModule)];

    // How long each aura-granted stack refreshes Poison's duration to, in the same "ticks"
    // unit PoisonSystem itself counts down (once per its own PoisonEffects.TickIntervalFrames
    // cycle, not per frame). Deliberately tiny (1), not some longer fixed window: ApplyStack
    // refreshes RemainingDurationTicks to Max(existing, durationInTicks) rather than adding,
    // so as long as the entity stays exposed, the aura's own re-grant cadence
    // (AuraEffects.TickIntervalFrames, the same 60 frames) keeps refreshing the duration back
    // up to at least 1 before it would otherwise hit 0 -- and once out of range, the duration
    // simply counts down and expires normally. A longer fixed duration here would let poison
    // outlive having left the aura by that many extra ticks for no reason.
    private const int AuraDurationTicks = 1;

    private EventBus _eventBus = null!;
    private IPlayerQuery? _playerQuery;
    private MathUtility _mathUtility = null!;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
        _mathUtility = context.MathUtility;
        context.StatusEffectAuraAppliers.Register(new TimerBasedAuraApplier<PoisonTimerComponent>(
            StatusEffectType.Poison,
            (componentManager, entityId, source) => PoisonEffects.ApplyStack(componentManager, entityId, source, AuraDurationTicks)));
        context.StatusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph,
            poison => poison.FramesUntilNextTick + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames));
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });

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

        systemManager.Register(new PoisonSystem(
            componentManager.GetPackedPool<PoisonTimerComponent>(),
            componentManager.GetMultiPool<StatusEffectStack>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            _eventBus,
            _playerQuery,
            _mathUtility,
            statModifiers,
            bodyParts));
    }
}
