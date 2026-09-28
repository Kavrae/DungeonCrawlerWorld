using Engine.ECS.Systems;
using Engine.Modules;
using Game.Modules.Core;
using Game.Modules.Paralysis.Components;
using Game.Modules.Paralysis.Systems;
using Game.Modules.StatusEffects;

namespace Game.Modules.Paralysis;

/// <summary>
/// Paralysis-specific: its own timer component and system, depending on StatusEffectsModule
/// (shared immunity storage). Registers a TimerBasedAuraApplier&lt;ParalysisTimerComponent&gt; into
/// the shared StatusEffectAuraApplierRegistry during Configure, so any future aura source (or a
/// StatusEffectGrant inside any IActionActivator's own ActionEffect) can grant Paralysis
/// without depending on this module directly. Paralysis has nothing to do with hit points, only
/// ActionLockComponent -- the concrete proof that a status effect can apply to
/// entities without hit points.
/// </summary>
public sealed class ParalysisModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000014");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [StatusEffectsModule.ModuleId, CoreModule.ModuleId];

    public void Configure(GameModuleContext context)
    {
        var componentManager = context.ComponentManager;
        var eventBus = context.EventBus;
        var playerQuery = context.PlayerQuery;
        var timers = componentManager.GetPackedPool<ParalysisTimerComponent>();

        context.StatusEffectAuraAppliers.Register(new TimerBasedAuraApplier<ParalysisTimerComponent>(
            StatusEffectType.Paralysis,
            timers,
            (entityId, source, now) => ParalysisEffects.Apply(componentManager, entityId, source, now, eventBus, playerQuery)));
        context.StatusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph,
            timers,
            static (paralysis, now) => FrameDeadline.Remaining(paralysis.ExpiresAtFrame, now)));
    }

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<ParalysisTimerComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        systemManager.Register(new ParalysisSystem(componentManager.GetPackedPool<ParalysisTimerComponent>()));
    }
}
