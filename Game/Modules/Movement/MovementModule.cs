using Engine.Modules;
using Game.Modules.BodyPartEffects;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffectAura;
using Game.Modules.StatusEffectAura.Components;

namespace Game.Modules.Movement;

/// <summary>The movement module for handling entity movement logic.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class MovementModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000004");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [CoreModule.ModuleId, DeathModule.ModuleId, StatusEffectAuraModule.ModuleId, StatModifiersModule.ModuleId, BodyPartEffectsModule.ModuleId, ProcessingTierModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<MovementComponent>(static (ref existing, incoming) =>
        {
            existing.MovementMode = (MovementMode)Math.Max((byte)existing.MovementMode, (byte)incoming.MovementMode);
            // The later deadline wins rather than averaging -- averaging two absolute frames would
            // invent a moment neither part asked for, the same rule ActionLockComponent's own merge uses.
            existing.WaitUntilFrame = Math.Max(existing.WaitUntilFrame, incoming.WaitUntilFrame);
            existing.NextMapPosition = incoming.NextMapPosition;
            existing.TargetMapPosition = incoming.TargetMapPosition;
        });
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        // Only used to widen MovementSystem's EventBus.Publish gate to an aura-carrying mover (see
        // MovementSystem's own doc comment).
        var auraSources = componentManager.GetMultiPool<StatusEffectAuraSourceComponent>();
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var movementDisabled = componentManager.GetPackedPool<MovementDisabledComponent>();

        systemManager.Register(new MovementSystem(
            componentManager.GetDirectPool<TransformComponent>(),
            componentManager.GetPackedPool<ActionLockComponent>(),
            componentManager.GetPackedPool<MovementComponent>(),
            context.MapQuery,
            context.EventBus,
            context.EntityMoveSync,
            context.MovedEntities,
            context.PlayerQuery,
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            context.ProcessingTierEvents,
            deadEntities,
            auraSources,
            statModifiers,
            movementDisabled));

        systemManager.RegisterFrameScoped(context.MovedEntities);
    }
}