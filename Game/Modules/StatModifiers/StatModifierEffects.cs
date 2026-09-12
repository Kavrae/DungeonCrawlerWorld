using Engine.ECS.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.StatModifiers;

/// <summary>Grants a new active modifier. Always an unconditional add -- no stacking cap, unlike Poison's MaxStacks, since none has been requested for stat modifiers.</summary>
public static class StatModifierEffects
{
    /// <param name="expiresAtFrame">The frame the modifier is removed on -- FrameDeadline.After(now, duration), or FrameDeadline.Never for a permanent one. Expiry scheduling is automatic: StatModifierExpirySystem watches the pool, so nothing else has to be written alongside it.</param>
    public static void Apply(
        ComponentManager componentManager,
        int entityId,
        StatModifierTarget target,
        StatModifierOperation operation,
        StatModifierPolarity polarity,
        bool canModify,
        float magnitude,
        uint expiresAtFrame,
        StatusEffectSource source,
        Tag? conditionTag = null) =>
        componentManager.GetMultiPool<StatModifierComponent>().Add(entityId, new StatModifierComponent(
            target, operation, polarity, canModify, magnitude, expiresAtFrame, source, conditionTag));
}
