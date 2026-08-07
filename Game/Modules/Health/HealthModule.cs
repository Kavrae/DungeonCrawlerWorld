using Engine.ECS.Components;
using Engine.ECS.Systems;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.StatModifiers.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Health;

public sealed class HealthModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000003");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    private ProcessingTierEvents _processingTierEvents = null!;

    public void Configure(GameModuleContext context) => _processingTierEvents = context.ProcessingTierEvents;

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) =>
        {
            // Floored at 0: a negative MaximumHealth here would make the Clamp below throw
            // (min > max), and "negative max health" isn't a meaningful state regardless of how
            // it arose (e.g. merging in a component that never validated Maximum* >= 0).
            existing.MaximumHealth = MathHelper.Clamp((existing.MaximumHealth + incoming.MaximumHealth) / 2f, 0f, float.MaxValue);
            existing.CurrentHealth = MathHelper.Clamp((existing.CurrentHealth + incoming.CurrentHealth) / 2f, 0f, existing.MaximumHealth);
        });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // StatModifierComponent may not be registered at all (e.g. a test building a minimal
        // module set without StatModifiersModule) -- HealthRegenSystem/HealthDamage both treat
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
        var abilityScores = componentManager.IsRegistered<AbilityScoreComponent>()
            ? componentManager.GetMultiPool<AbilityScoreComponent>()
            : null;

        systemManager.Register(new HealthRegenSystem(
            componentManager.GetPackedPool<HealthComponent>(),
            componentManager.GetDirectPool<ProcessingTierComponent>(),
            _processingTierEvents,
            statModifiers,
            deadEntities,
            abilityScores));
    }
}