using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.BodyPartEffects.Systems;
using Game.Modules.Health.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.BodyPartEffects;

/// <summary>
/// Owns the two marker components (MovementDisabledComponent/MeleeDisabledComponent) and the
/// system that keeps them, plus StatModifierTarget.MovementLockFrames/OutgoingDamage (the latter
/// scoped to Tag.Melee via StatModifierComponent.ConditionTag), in sync with an entity's own body-part condition -- see PLAN-body-part-gameplay-effects.md and
/// BodyPartEffectsSystem's own doc comment for the full design. Depends on HealthModule for
/// BodyPartComponent -- registers its own components regardless (an entity set with no Complex-health
/// race loaded just never populates BodyPartComponent, so BodyPartEffectsSystem's stripe set stays
/// empty, mirroring ComplexHealthRegenSystem's own "always registered, empty until populated" precedent).
/// </summary>
public sealed class BodyPartEffectsModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000012");

    // No hard Dependencies on HealthModule/StatModifiersModule -- neither type is a safe
    // Dependencies target anywhere in this codebase, since a mod can legitimately replace
    // HealthModule by Id (see Mods.TestFixtures.ReplacementHealthModule), which would make a
    // hard typeof(HealthModule) dependency fail topo-sort even though the replacement still
    // provides BodyPartComponent. Both pools are checked softly below instead, the same
    // IsRegistered/optional-pool pattern BurningModule/ActionsModule/MovementModule already use.
    public IReadOnlyList<Type> Dependencies { get; } = [];

    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context)
    {
        _processingTierEvents = context.ProcessingTierEvents;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<MovementDisabledComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<MeleeDisabledComponent>(static (ref existing, incoming) => { });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        if (!componentManager.IsRegistered<BodyPartComponent>())
        {
            return;
        }

        var statModifiers = componentManager.GetOptionalMultiPool<StatModifierComponent>();

        systemManager.Register(new BodyPartEffectsSystem(
            componentManager.GetMultiPool<BodyPartComponent>(),
            componentManager.GetPackedPool<MovementDisabledComponent>(),
            componentManager.GetPackedPool<MeleeDisabledComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            statModifiers));
    }
}
