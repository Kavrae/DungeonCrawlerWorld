using Engine.Math;
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

    /// <summary>The stream a poison tick picks the body part it lands on from.</summary>
    public static readonly RandomStreamKey TickRandomStream = new(ModuleId, "Ticks");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [StatusEffectsModule.ModuleId, HealthModule.ModuleId, StatModifiersModule.ModuleId, RaceModule.ModuleId, DeathModule.ModuleId];

    public void Configure(GameModuleContext context)
    {
        var componentManager = context.ComponentManager;
        var eventBus = context.EventBus;
        var playerQuery = context.PlayerQuery;
        var timers = componentManager.GetPackedPool<PoisonTimerComponent>();

        context.StatusEffectAppliers.Register(new TimerBasedStatusEffectApplier<PoisonTimerComponent>(
            StatusEffectType.Poison,
            timers,
            PoisonEffects.MaxStacks,
            takesDuration: true,
            (entityId, count, source, now, announcesRefusal, immunityAlreadyChecked, durationTicks) => PoisonEffects.ApplyStacks(componentManager, entityId, count, source, durationTicks, now, eventBus, playerQuery, announcesRefusal, immunityAlreadyChecked)));
        context.StatusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph, PoisonEffects.MaxStacks,
            timers,
            static (poison, now) => TimedStatusEffectStacks.RemainingFrames(poison, now, PoisonEffects.TickIntervalFrames),
            static poison => poison.Source));
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
            context.RandomStreams.Get(TickRandomStream),
            statModifiers,
            bodyParts,
            componentManager.GetPackedPool<DeadComponent>(),
            DamageLedger.For(componentManager, context.EntityKeys),
            context.FloatingTextFeed));
    }
}
