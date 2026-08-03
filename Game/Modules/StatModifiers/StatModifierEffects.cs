using Engine.ECS.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.StatModifiers;

/// <summary>Grants a new active modifier. Always an unconditional add -- no stacking cap, unlike Poison's MaxStacks, since none has been requested for stat modifiers.</summary>
public static class StatModifierEffects
{
    public static void Apply(
        ComponentManager componentManager,
        int entityId,
        StatModifierTarget target,
        StatModifierOperation operation,
        StatModifierPolarity polarity,
        bool canModify,
        float magnitude,
        int durationFrames,
        StatusEffectSource source)
    {
        componentManager.GetMultiPool<StatModifierComponent>().Add(entityId, new StatModifierComponent(
            target, operation, polarity, canModify, magnitude, durationFrames, source));
    }
}
