using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.Core.Components;
using Game.Modules.Paralysis.Components;
using Game.Modules.Paralysis.Systems;
using Game.Modules.StatusEffects;
using Game.World;
using Game.Modules.Core;

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

    private EventBus _eventBus = null!;
    private IPlayerQuery _playerQuery = null!;

    public void Configure(GameModuleContext context)
    {
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;

        context.StatusEffectAuraAppliers.Register(new TimerBasedAuraApplier<ParalysisTimerComponent>(
            StatusEffectType.Paralysis,
            (componentManager, entityId, source, now) => ParalysisEffects.Apply(componentManager, entityId, source, now, _eventBus, _playerQuery)));
        context.StatusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph,
            static (paralysis, now) => FrameDeadline.Remaining(paralysis.ExpiresAtFrame, now)));
    }

    public void RegisterComponents(ComponentManager componentManager) =>
        componentManager.RegisterPackedPool<ParalysisTimerComponent>(static (ref existing, incoming) => { });

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new ParalysisSystem(componentManager.GetPackedPool<ParalysisTimerComponent>()));
    }
}
