using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Modules;
using Game.Modules.Energy.Components;
using Game.Modules.Energy.Systems;

namespace Game.Modules.Energy;

public sealed class EnergyModule : IModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000002");

    public void RegisterComponents(ComponentManager componentManager)
    {
        componentManager.RegisterPackedPool<EnergyComponent>(static (ref EnergyComponent existing, EnergyComponent incoming) =>
        {
            existing.EnergyRecharge = (short)((existing.EnergyRecharge + incoming.EnergyRecharge) / 2);
            // Floored at 0: a negative MaximumEnergy here would make the ClampShort below
            // throw (min > max), and "negative max energy" isn't a meaningful state regardless
            // of how it arose (e.g. merging in a component that never validated Maximum* >= 0).
            existing.MaximumEnergy = MathUtility.ClampShort((short)((existing.MaximumEnergy + incoming.MaximumEnergy) / 2), 0, short.MaxValue);
            existing.CurrentEnergy = MathUtility.ClampShort((short)((existing.CurrentEnergy + incoming.CurrentEnergy) / 2), 0, existing.MaximumEnergy);
        });
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        systemManager.Register(new EnergyRechargeSystem(componentManager.GetPackedPool<EnergyComponent>()));
    }
}
