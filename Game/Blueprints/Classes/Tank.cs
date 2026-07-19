using Engine.ECS.Components;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;

namespace Game.Blueprints.Classes;

/// <summary>Tanks have 10% more health and health regen than their race baseline.</summary>
public sealed class Tank : IBlueprint
{
    private static readonly Guid ClassId = new("45ddf671-3f76-4e23-9ac3-7a588282ec35");
    private const string ClassName = "Tank";
    private const string Description = "Extra hit points";

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ClassComponent(ClassId, ClassName, Description));

        componentManager.TryUpdate(entityId, static (ref HealthComponent healthComponent) =>
        {
            healthComponent.MaximumHealth = (short)(healthComponent.MaximumHealth * 1.10m);
            healthComponent.HealthRegen = (short)(healthComponent.HealthRegen * 1.10m);
        });

        componentManager.Merge(entityId, new DisplayTextComponent(ClassName, "Tank class"));
    }
}
