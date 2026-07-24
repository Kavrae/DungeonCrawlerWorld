using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;

namespace Game.Blueprints.Classes;

/// <summary>
/// Tanks have 10% more health and health regen than their race baseline. Order-independent:
/// if a race blueprint already set HealthComponent, Tank boosts it in place; if not (Tank
/// built standalone, or composed before a race), Tank merges in its own baseline instead, so
/// the class's mechanic still functions rather than silently doing nothing because of
/// composition order.
/// </summary>
public sealed class Tank(MathUtility mathUtility) : IBlueprint
{
    private static readonly Guid ClassId = new("45ddf671-3f76-4e23-9ac3-7a588282ec35");
    private const string ClassName = "Tank";
    private const string Description = "Extra hit points";

    private const short BaselineHealthRegen = 10;
    private const short BaselineMaximumHealth = 100;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ClassComponent(ClassId, ClassName, Description));

        if (componentManager.GetPackedPool<HealthComponent>().Has(entityId))
        {
            componentManager.TryUpdate(entityId, static (ref HealthComponent healthComponent) =>
            {
                healthComponent.MaximumHealth = (short)(healthComponent.MaximumHealth * 1.10m);
                healthComponent.HealthRegen = (short)(healthComponent.HealthRegen * 1.10m);
            });
        }
        else
        {
            componentManager.Merge(entityId, new HealthComponent((short)mathUtility.Next(1, BaselineMaximumHealth + 1), BaselineHealthRegen, BaselineMaximumHealth));
        }

        componentManager.Merge(entityId, new DisplayTextComponent(ClassName, "Tank class"));
    }
}