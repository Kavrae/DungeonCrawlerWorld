using Engine.ECS.Components;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Energy.Components;

namespace Game.Blueprints.Classes;

/// <summary>Engineers have 5% more energy and energy recharge than their race baseline.</summary>
public sealed class Engineer : IBlueprint
{
    private static readonly Guid ClassId = new("7b97d17d-5e77-42a1-8b4a-ed0bb97c730d");
    private const string ClassName = "Engineer";
    private const string Description = "TODO default engineer description";

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ClassComponent(ClassId, ClassName, Description));

        componentManager.TryUpdate(entityId, static (ref EnergyComponent energyComponent) =>
        {
            energyComponent.MaximumEnergy = (short)(energyComponent.MaximumEnergy * 1.05m);
            energyComponent.EnergyRecharge = (short)(energyComponent.EnergyRecharge * 1.05m);
        });

        componentManager.Merge(entityId, new DisplayTextComponent(ClassName, Description));
    }
}
