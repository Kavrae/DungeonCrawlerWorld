using Engine.ECS.Components.Stores;
using Engine.Tags;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions;

/// <summary>Grants and reads the Disabled status: rules on the user (DisabledActivationComponent) disabling the actions and items whose tags match.</summary>
public static class DisabledActivations
{
    /// <summary>Whether entityId holds a rule disabling definition.</summary>
    public static bool IsDisabled(MultiComponentPool<DisabledActivationComponent> rules, int entityId, ActivatableDefinition definition)
    {
        for (var denseIndex = rules.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = rules.GetNextDenseIndex(denseIndex))
        {
            if (rules.GetReadonlyByDenseIndex(denseIndex).Query.Matches(definition.Tags))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Disables what query matches for entityId until expiresAtFrame: a rule from the same query is extended (never shortened) rather than added again.</summary>
    /// <remarks>A new rule takes the key after the highest the entity holds, so a key is its own among the entity's rules and depends on nothing outside them.</remarks>
    public static void Grant(MultiComponentPool<DisabledActivationComponent> rules, int entityId, GameplayTagQuery query, uint expiresAtFrame)
    {
        var highestInstanceKey = 0;
        for (var denseIndex = rules.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = rules.GetNextDenseIndex(denseIndex))
        {
            ref readonly var held = ref rules.GetReadonlyByDenseIndex(denseIndex);
            if (!ReferenceEquals(held.Query, query))
            {
                highestInstanceKey = System.Math.Max(highestInstanceKey, held.InstanceKey);
                continue;
            }

            using var rule = rules.WriteByDenseIndex(denseIndex);
            rule.Value.ExpiresAtFrame = System.Math.Max(rule.Value.ExpiresAtFrame, expiresAtFrame);
            return;
        }

        rules.Add(entityId, new DisabledActivationComponent(query, expiresAtFrame, highestInstanceKey + 1));
    }
}
