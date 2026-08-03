using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Modules;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Health;

public sealed class HealthModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000003");

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) =>
        {
            existing.HealthRegen = (short)((existing.HealthRegen + incoming.HealthRegen) / 2);
            // Floored at 0: a negative MaximumHealth here would make the ClampShort below
            // throw (min > max), and "negative max health" isn't a meaningful state regardless
            // of how it arose (e.g. merging in a component that never validated Maximum* >= 0).
            existing.MaximumHealth = MathUtility.ClampShort((short)((existing.MaximumHealth + incoming.MaximumHealth) / 2), 0, short.MaxValue);
            existing.CurrentHealth = MathUtility.ClampShort((short)((existing.CurrentHealth + incoming.CurrentHealth) / 2), 0, existing.MaximumHealth);
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

        systemManager.Register(new HealthRegenSystem(componentManager.GetPackedPool<HealthComponent>(), statModifiers));
    }
}