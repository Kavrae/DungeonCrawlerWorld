using Engine.Modules;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.BodyPartEffects.Systems;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.BodyPartEffects;

/// <summary>
/// Owns the two marker components (MovementDisabledComponent/MeleeDisabledComponent) and the
/// system that keeps them, plus StatModifierTarget.MovementLockFrames/OutgoingDamage (the latter
/// scoped to Tag.Melee via StatModifierComponent.ConditionTag), in sync with an entity's own body-part condition -- see
/// BodyPartEffectsSystem's own doc comment for the full design. Requires HealthModule for
/// BodyPartStateComponent. An entity set with no Complex-health race never populates it, so
/// BodyPartEffectsSystem's stripe set stays empty.
/// </summary>
public sealed class BodyPartEffectsModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000012");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [HealthModule.ModuleId, StatModifiersModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId];

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<MovementDisabledComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<MeleeDisabledComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();

        systemManager.Register(new BodyPartEffectsSystem(
            EntityBodyParts.For(componentManager, context.Definitions),
            componentManager.GetPackedPool<BodyPartStateComponent>(),
            componentManager.GetPackedPool<MovementDisabledComponent>(),
            componentManager.GetPackedPool<MeleeDisabledComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            context.ProcessingTierEvents,
            statModifiers));
    }
}
