using Engine.Math;
using Game.Effects;
using Game.Modules.Actions.Activators;

namespace Game.Modules.Actions;

/// <summary>Checks that every toggle the build's registered content defines can be switched off again.</summary>
/// <remarks>
/// Run once every module is configured, beside ContentTagValidation, so a mod defining a toggle that
/// breaks a rule fails its dry run instead of leaving something on its first holder for good.
/// </remarks>
internal static class ToggleContentValidation
{
    /// <exception cref="InvalidOperationException">A definition breaks a toggle rule, naming the definition and the rule.</exception>
    public static void EnsureValid(GameModuleContext context)
    {
        foreach (var action in context.Actions.Definitions)
        {
            var heldBy = $"Action '{action.Name}'";

            // A toggle applies to whoever has it on, so there is no other target for it to resolve.
            if (action.Toggle is not null && action.Activator.Targeting.Shape != TargetShape.Self)
            {
                throw new InvalidOperationException($"{heldBy} is a toggle but is not Self-targeted: a toggle applies to its holder.");
            }

            EnsureHeldEffectsCanBeReverted(action, heldBy);
        }

        foreach (var item in context.Items.Definitions)
        {
            var heldBy = $"Item '{item.Name}'";
            var hasToggleActivator = item.Activator is ToggleItemActivator;

            if (hasToggleActivator && item.Toggle is null)
            {
                throw new InvalidOperationException($"{heldBy} has a ToggleItemActivator but no ToggleSpec: an item is a toggle only with both.");
            }

            if (!hasToggleActivator && item.Toggle is not null)
            {
                throw new InvalidOperationException($"{heldBy} has a ToggleSpec but its activator is not a ToggleItemActivator: an item is a toggle only with both.");
            }

            EnsureHeldEffectsCanBeReverted(item, heldBy);
        }
    }

    /// <summary>A toggle's held effects may grant something with no end only through an entry that can take it back.</summary>
    private static void EnsureHeldEffectsCanBeReverted(ActivatableDefinition definition, string heldBy)
    {
        if (definition.Toggle is not null)
        {
            EnsureCanBeReverted(definition.Effects, heldBy);
        }
    }

    private static void EnsureCanBeReverted(IReadOnlyList<Effect> effects, string heldBy)
    {
        foreach (var effect in effects)
        {
            foreach (var entry in effect.Entries)
            {
                if (entry.GrantsUntilRevoked && entry is not IReversibleEffectEntry)
                {
                    throw new InvalidOperationException($"{heldBy} is a toggle holding a {entry.GetType().Name} with no end, which can't be taken back when the toggle is switched off.");
                }

                EnsureCanBeReverted(entry.NestedEffects, heldBy);
            }
        }
    }
}
