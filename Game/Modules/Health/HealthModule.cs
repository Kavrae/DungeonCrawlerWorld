using Engine.ECS.Components;
using Engine.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health;

public sealed class HealthModule : IGameModule
{
    public static readonly Guid ModuleId = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000003");

    public Guid Id => ModuleId;

    public IReadOnlyList<Guid> Requires { get; } = [StatModifiersModule.ModuleId, DeathModule.ModuleId, AbilityScoresModule.ModuleId, ProcessingTierModule.ModuleId, RaceModule.ModuleId];

    /// <summary>The entity whose MaximumHealth sums were snapshotted by the most recent StatModifierExpiringEvent, and those sums -- consumed by the matching StatModifierExpiredEvent. See MaximumHealthShift.</summary>
    /// <remarks>A single slot rather than a map: StatModifierExpirySystem sweeps one entity at a time and publishes both events synchronously within that sweep, so a snapshot never has to outlive the entity it was taken for.</remarks>
    private int _expiringEntityId = -1;
    private float _expiringAdditiveSum;
    private float _expiringMultiplicativeSum;

    public void RegisterComponents(ComponentRegistration registration)
    {
        var componentManager = registration.ComponentManager;

        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) =>
        {
            // Floored at 0: a negative MaximumHealth here would make the Clamp below throw
            // (min > max), and "negative max health" isn't a meaningful state regardless of how
            // it arose (e.g. merging in a component that never validated Maximum* >= 0).
            existing.MaximumHealth = MathHelper.Clamp((existing.MaximumHealth + incoming.MaximumHealth) / 2f, 0f, float.MaxValue);
            existing.CurrentHealth = MathHelper.Clamp((existing.CurrentHealth + incoming.CurrentHealth) / 2f, 0f, existing.MaximumHealth);
        });

        // Only an entity something has actually happened to holds one -- see BodyPartStateComponent.
        componentManager.RegisterPackedPool<BodyPartStateComponent>(static (ref existing, incoming) => existing = incoming, initialCapacity: 20_000);
    }

    public void RegisterSystems(SystemRegistration<GameModuleContext> registration)
    {
        var context = registration.Context;
        var systemManager = registration.SystemManager;
        var componentManager = registration.ComponentManager;

        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var deadEntities = componentManager.GetPackedPool<DeadComponent>();
        var abilityScores = componentManager.GetPackedPool<AbilityScoresComponent>();

        systemManager.Register(new SimpleHealthRegenSystem(
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            context.ProcessingTierEvents,
            statModifiers,
            deadEntities,
            abilityScores,
            EntityBodyParts.For(componentManager, context.Definitions),
            context.EventBus,
            context.PlayerQuery,
            context.FloatingTextFeed));

        systemManager.Register(new ComplexHealthRegenSystem(
            EntityBodyParts.For(componentManager, context.Definitions),
            componentManager.GetPackedPool<BodyPartStateComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            context.ProcessingTierEvents,
            statModifiers,
            deadEntities,
            abilityScores,
            context.EventBus,
            context.PlayerQuery,
            context.FloatingTextFeed));

        WireMaximumHealthShift(componentManager, context);
    }

    /// <summary>Current health follows the effective maximum when a MaximumHealth modifier wears off, the expiry half of MaximumHealthShift -- the grant half is MaximumHealthShift.ApplyModifier, called by whoever grants one.</summary>
    /// <remarks>Two events because the amount to give back is the distance the maximum moved, and that is only knowable from both sides of the removal: the sums are snapshotted while the modifiers are still there, and spent once they are gone.</remarks>
    private void WireMaximumHealthShift(ComponentManager componentManager, GameModuleContext context)
    {
        context.EventBus.Subscribe<StatModifierExpiringEvent>(expiring =>
        {
            _expiringEntityId = expiring.EntityId;
            MaximumHealthShift.Capture(componentManager, expiring.EntityId, out _expiringAdditiveSum, out _expiringMultiplicativeSum);
        });

        context.EventBus.Subscribe<StatModifierExpiredEvent>(expired =>
        {
            if (expired.Target != StatModifierTarget.MaximumHealth || expired.EntityId != _expiringEntityId)
            {
                return;
            }

            // Cleared first: a sweep that expires two MaximumHealth modifiers at once publishes two
            // of these, and the snapshot already covers both, so only the first may spend it.
            _expiringEntityId = -1;
            MaximumHealthShift.Apply(componentManager, context.Definitions, expired.EntityId, _expiringAdditiveSum, _expiringMultiplicativeSum);
        });
    }
}