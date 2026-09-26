using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Game.Blueprints;

namespace Game.Modules.Health;

public sealed class HealthModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000003");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private BlueprintRegistry _creatures = null!;
    private ProcessingTierEvents _processingTierEvents = null!;
    private MathUtility _mathUtility = null!;
    private EventBus _eventBus = null!;
    private IPlayerQuery? _playerQuery;

    /// <summary>The entity whose MaximumHealth sums were snapshotted by the most recent StatModifierExpiringEvent, and those sums -- consumed by the matching StatModifierExpiredEvent. See MaximumHealthShift.</summary>
    /// <remarks>A single slot rather than a map: StatModifierExpirySystem sweeps one entity at a time and publishes both events synchronously within that sweep, so a snapshot never has to outlive the entity it was taken for.</remarks>
    private int _expiringEntityId = -1;
    private float _expiringAdditiveSum;
    private float _expiringMultiplicativeSum;

    public void Configure(GameModuleContext context)
    {
        _creatures = context.Definitions;
        _processingTierEvents = context.ProcessingTierEvents;
        _mathUtility = context.MathUtility;
        _eventBus = context.EventBus;
        _playerQuery = context.PlayerQuery;
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
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

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // StatModifierComponent may not be registered at all (e.g. a test building a minimal
        // module set without StatModifiersModule) -- SimpleHealthRegenSystem/HealthDamage both treat
        // a null pool the same as "no active modifiers" (StatModifierMath.GetEffectiveValue
        // returns the base value unchanged), so this stays optional rather than a hard
        // Dependencies requirement that would force every such module list to include it.
        var statModifiers = componentManager.IsRegistered<StatModifierComponent>()
            ? componentManager.GetMultiPool<StatModifierComponent>()
            : null;
        var deadEntities = componentManager.IsRegistered<DeadComponent>()
            ? componentManager.GetPackedPool<DeadComponent>()
            : null;
        // Optional for the same reason statModifiers/deadEntities are -- a module set built
        // without AbilityScoresModule (e.g. a minimal test) still works, just with 0 regen
        // (no Constitution total found) rather than a hard dependency.
        var abilityScores = componentManager.IsRegistered<AbilityScoresComponent>()
            ? componentManager.GetPackedPool<AbilityScoresComponent>()
            : null;
        // Optional -- BurningModule might not be loaded at all (see BodyPartBurningTimerComponent's
        // own doc comment for why it's registered here, under Health, rather than under Burning).
        var bodyPartBurningTimers = componentManager.IsRegistered<BodyPartBurningTimerComponent>()
            ? componentManager.GetMultiPool<BodyPartBurningTimerComponent>()
            : null;

        systemManager.Register(new SimpleHealthRegenSystem(
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            statModifiers,
            deadEntities,
            abilityScores,
            _eventBus,
            _playerQuery));

        // Always registered -- RegisterComponents always calls RegisterMultiPool<BodyPartComponent>(), unlike the genuinely-optional pools above.
        systemManager.Register(new ComplexHealthRegenSystem(
            EntityBodyParts.For(componentManager, _creatures),
            componentManager.GetPackedPool<BodyPartStateComponent>(),
            componentManager.GetPackedPool<SimpleHealthComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            statModifiers,
            deadEntities,
            abilityScores,
            bodyPartBurningTimers,
            _eventBus,
            _playerQuery));

        WireMaximumHealthShift(componentManager);
    }

    /// <summary>Current health follows the effective maximum when a MaximumHealth modifier wears off, the expiry half of MaximumHealthShift -- the grant half is MaximumHealthShift.ApplyModifier, called by whoever grants one.</summary>
    /// <remarks>Two events because the amount to give back is the distance the maximum moved, and that is only knowable from both sides of the removal: the sums are snapshotted while the modifiers are still there, and spent once they are gone.</remarks>
    private void WireMaximumHealthShift(ComponentManager componentManager)
    {
        _eventBus.Subscribe<StatModifierExpiringEvent>(expiring =>
        {
            _expiringEntityId = expiring.EntityId;
            MaximumHealthShift.Capture(componentManager, expiring.EntityId, out _expiringAdditiveSum, out _expiringMultiplicativeSum);
        });

        _eventBus.Subscribe<StatModifierExpiredEvent>(expired =>
        {
            if (expired.Target != StatModifierTarget.MaximumHealth || expired.EntityId != _expiringEntityId)
            {
                return;
            }

            // Cleared first: a sweep that expires two MaximumHealth modifiers at once publishes two
            // of these, and the snapshot already covers both, so only the first may spend it.
            _expiringEntityId = -1;
            MaximumHealthShift.Apply(componentManager, _creatures, expired.EntityId, _expiringAdditiveSum, _expiringMultiplicativeSum);
        });
    }
}