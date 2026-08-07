using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Class.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Blueprints.Classes;

/// <summary>
/// Tanks have 10% more max health than their race baseline, plus a permanent +10% HealthRegen
/// StatModifier (regen has no stored field to multiply in place now that it's computed live from
/// Constitution -- see HealthRegenSystem -- so the bonus is granted as a modifier on
/// StatModifierTarget.HealthRegen instead, the same way PlayerBlueprint's own permanent bonuses
/// are granted). Order-independent: if a race blueprint already set HealthComponent, Tank boosts
/// its MaximumHealth in place; if not (Tank built standalone, or composed before a race), Tank
/// merges in its own baseline instead, so the class's mechanic still functions rather than
/// silently doing nothing because of composition order. The regen modifier is granted either way.
/// </summary>
public sealed class Tank(MathUtility mathUtility) : IBlueprint
{
    private static readonly Guid ClassId = new("45ddf671-3f76-4e23-9ac3-7a588282ec35");
    private const string ClassName = "Tank";
    private const string Description = "Extra hit points";

    private const short BaselineMaximumHealth = 100;
    private const float HealthRegenBonusMultiplier = 0.10f;

    public void Build(ComponentManager componentManager, int entityId)
    {
        componentManager.Merge(entityId, new ClassComponent(ClassId, ClassName, Description));

        if (componentManager.GetPackedPool<HealthComponent>().Has(entityId))
        {
            componentManager.TryUpdate(entityId, static (ref HealthComponent healthComponent) =>
            {
                healthComponent.MaximumHealth *= 1.10f;
            });
        }
        else
        {
            componentManager.Merge(entityId, new HealthComponent((short)mathUtility.Next(1, BaselineMaximumHealth + 1), BaselineMaximumHealth));
        }

        StatModifierEffects.Apply(componentManager, entityId, StatModifierTarget.HealthRegen, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: HealthRegenBonusMultiplier, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.FromEntity(entityId));

        componentManager.Merge(entityId, new DisplayTextComponent(ClassName, "Tank class"));
    }
}