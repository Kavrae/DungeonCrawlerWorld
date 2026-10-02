using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Tags;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.StatModifiers;

/// <summary>Grants stat modifiers: Apply always adds a new one, ApplyOrRefresh keeps one per source.</summary>
/// <remarks>There is no stacking cap, unlike Poison's MaxStacks, since none has been requested for stat modifiers.</remarks>
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
        ActionSource source,
        GameplayTag conditionTag = default) =>
        componentManager.GetMultiPool<StatModifierComponent>().Add(entityId, new StatModifierComponent(
            target, operation, polarity, canModify, magnitude, expiresAtFrame, source, conditionTag));

    /// <summary>Grants the modifier, or -- if entityId already holds the same modifier from the same source -- moves that one's expiry to expiresAtFrame instead of adding a second.</summary>
    /// <remarks>
    /// "The same" is every field but the expiry: target, operation, polarity, magnitude, condition tag
    /// and source. So a buff an entity keeps being given by one thing (terrain it stands on) is held
    /// once and kept topped up, while the same buff from something else stacks with it as any other
    /// modifier would. The expiry is replaced, not extended: a refresh can shorten it. The entity's
    /// expiry timer follows on its own -- StatModifierExpirySystem watches the pool.
    /// </remarks>
    public static void ApplyOrRefresh(
        MultiComponentPool<StatModifierComponent> statModifiers,
        int entityId,
        StatModifierTarget target,
        StatModifierOperation operation,
        StatModifierPolarity polarity,
        bool canModify,
        float magnitude,
        uint expiresAtFrame,
        ActionSource source,
        GameplayTag conditionTag = default)
    {
        var modifier = new StatModifierComponent(target, operation, polarity, canModify, magnitude, expiresAtFrame, source, conditionTag);

        var refreshed = statModifiers.TryUpdateFirst(
            entityId,
            modifier,
            static (ref readonly StatModifierComponent held, StatModifierComponent granted) =>
                held.Target == granted.Target
                && held.Operation == granted.Operation
                && held.Polarity == granted.Polarity
                && held.Magnitude == granted.Magnitude
                && held.ConditionTag == granted.ConditionTag
                && held.Source == granted.Source,
            static (ref StatModifierComponent held, StatModifierComponent granted) => held.ExpiresAtFrame = granted.ExpiresAtFrame);

        if (!refreshed)
        {
            statModifiers.Add(entityId, modifier);
        }
    }
}
