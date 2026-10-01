using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Poison.Components;
using Game.Modules.Poison.Systems;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;

namespace Game.Modules.Poison;

/// <summary>
/// Poison-specific: its own timer component and system, depending on StatusEffectsModule
/// (shared immunity storage). Registers a
/// TimerBasedStatusEffectApplier&lt;PoisonTimerComponent&gt; into the shared
/// StatusEffectApplierRegistry during Configure, so anything can grant
/// Poison stacks without depending on this module directly.
/// </summary>
public sealed class PoisonModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000009");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [StatusEffectsModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, RaceModule.ModuleId, DeathModule.ModuleId];

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

    public void Configure(GameModuleContext context)
    {
        var componentManager = context.ComponentManager;
        var entityKeys = context.EntityKeys;
        var eventBus = context.EventBus;
        var playerQuery = context.PlayerQuery;
        var timers = componentManager.GetPackedPool<PoisonTimerComponent>();

        context.StatusEffectAppliers.Register(new TimerBasedStatusEffectApplier<PoisonTimerComponent>(
            StatusEffectType.Poison,
            timers,
            PoisonEffects.MaxStacks,
            (entityId, count, source, now, announcesRefusal) => PoisonEffects.ApplyStacks(componentManager, entityKeys, entityId, count, source, AuraDurationTicks, now, eventBus, playerQuery, announcesRefusal)));
        context.StatusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph,
            timers,
            static (poison, now) => FrameDeadline.Remaining(poison.NextTickFrame, now) + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames));
    }

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var bodyParts = EntityBodyParts.For(componentManager, context.Definitions);

        systemManager.Register(new PoisonSystem(
            componentManager.GetPackedPool<PoisonTimerComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            context.EventBus,
            context.PlayerQuery,
            context.MathUtility,
            statModifiers,
            bodyParts,
            componentManager.GetPackedPool<DeadComponent>(),
            context.FloatingTextFeed));
    }
}
