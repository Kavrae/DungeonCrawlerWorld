using Engine.ECS.Components;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Energy.Components;

namespace Game.Blueprints.Classes;

/// <summary>
/// Engineers have 5% more energy and energy recharge than their race baseline. Order-
/// independent: if a race blueprint already set EnergyComponent, Engineer boosts it in
/// place; if not (Engineer built standalone, or composed before a race), Engineer merges in
/// its own baseline instead, so the class's mechanic still functions rather than silently
/// doing nothing because of composition order.
/// </summary>
public sealed class Engineer : IBlueprint
{
    private static readonly Guid ClassId = new("7b97d17d-5e77-42a1-8b4a-ed0bb97c730d");
    private const string ClassName = "Engineer";
    private const string Description = "TODO default engineer description";

    private const short BaselineCurrentEnergy = 50;
    private const short BaselineEnergyRecharge = 5;
    private const short BaselineMaximumEnergy = 100;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ClassComponent(ClassId, ClassName, Description));

        if (componentManager.GetPackedPool<EnergyComponent>().Has(entityId))
        {
            componentManager.TryUpdate(entityId, static (ref EnergyComponent energyComponent) =>
            {
                energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.05m);
                energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.05m);
            });
        }
        else
        {
            componentManager.Merge(entityId, new EnergyComponent(BaselineCurrentEnergy, BaselineEnergyRecharge, BaselineMaximumEnergy));
        }

        componentManager.Merge(entityId, new DisplayTextComponent(ClassName, Description));
    }
}