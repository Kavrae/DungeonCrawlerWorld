using Game.Effects;

namespace Game.Modules.Actions;

/// <summary>Whether an action or item asks for its user's mana at all, read from the effects it applies to its user.</summary>
/// <remarks>A cost is a mana IResourceDrain among the definition's ActivationEffects; a toggle's upkeep is one among its periodic effects. What it costs now is ActivationCosts'.</remarks>
public static class ManaUse
{
    /// <summary>Whether using definition ever drains its user's mana: as an activation effect or a toggle's upkeep, nested ones included.</summary>
    /// <remarks>What decides that an entity granted it gains a mana pool (ActionGrantEffects, EntityBuilder).</remarks>
    public static bool DrainsUsersMana(ActivatableDefinition definition) =>
        HoldsManaDrain(definition.ActivationEffects) || (definition.Toggle?.Periodic is { } periodic && HoldsManaDrain(periodic.Effects));

    private static bool HoldsManaDrain(IReadOnlyList<Effect> effects)
    {
        foreach (var effect in effects)
        {
            foreach (var entry in effect.Entries)
            {
                if (entry is IResourceDrain { Resource: DrainedResource.Mana } || HoldsManaDrain(entry.NestedEffects))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
