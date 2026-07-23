using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Modules;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;

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
        systemManager.Register(new HealthRegenSystem(componentManager.GetPackedPool<HealthComponent>()));
    }
}