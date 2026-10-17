using Game.Effects;

namespace Game.Modules.Actions;

/// <summary>What using something takes from its user now, per resource, through the user's modifiers and never rounded.</summary>
public readonly record struct ActivationCostTotals(float Mana, float Health)
{
    /// <summary>Whether using it takes nothing at all.</summary>
    public bool IsFree => Mana <= 0 && Health <= 0;
}

/// <summary>Reads what a definition's ActivationEffects take from its user: every IResourceDrain among them, by the resource it drains.</summary>
public static class ActivationCosts
{
    /// <summary>What using definition takes from entityId now.</summary>
    /// <remarks>Only the entries the activation effects hold directly: one a ChainedEffect may trigger is not a cost the user can count on paying.</remarks>
    public static ActivationCostTotals Of(EffectServices effectServices, int entityId, ActivatableDefinition definition, long now)
    {
        if (definition.ActivationEffects.Count == 0)
        {
            return default;
        }

        var context = ActivationEffectsApplier.UserContext(effectServices, entityId, definition, now);
        var mana = 0f;
        var health = 0f;
        foreach (var effect in definition.ActivationEffects)
        {
            foreach (var entry in effect.Entries)
            {
                if (entry is not IResourceDrain drain)
                {
                    continue;
                }

                var amount = drain.AmountFor(in context);
                if (drain.Resource == DrainedResource.Mana)
                {
                    mana += amount;
                }
                else
                {
                    health += amount;
                }
            }
        }

        return new ActivationCostTotals(mana, health);
    }
}
