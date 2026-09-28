using Engine.ECS.Components;
using Game.Modules.Health;
using Engine.ECS.Systems;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.BodyPartEffects.Systems;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Game.Blueprints;
using Game.Modules.StatModifiers;
using Game.Modules.Race;

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
    private BlueprintRegistry _creatures = null!;

    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000012");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [HealthModule.ModuleId, StatModifiersModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId];

    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context)
    {
        _creatures = context.Definitions;
        _processingTierEvents = context.ProcessingTierEvents;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<MovementDisabledComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<MeleeDisabledComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();

        systemManager.Register(new BodyPartEffectsSystem(
            EntityBodyParts.For(componentManager, _creatures),
            componentManager.GetPackedPool<BodyPartStateComponent>(),
            componentManager.GetPackedPool<MovementDisabledComponent>(),
            componentManager.GetPackedPool<MeleeDisabledComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            statModifiers));
    }
}
