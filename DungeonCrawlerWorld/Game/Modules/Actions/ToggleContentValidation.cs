using Game.Effects;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;

namespace Game.Modules.Actions;

/// <summary>Checks that every toggle the build's registered content defines can be switched off again.</summary>
/// <remarks>
/// Run once every module is configured, beside ContentTagValidation, so a mod defining a toggle that
/// breaks a rule fails its dry run instead of leaving something on its first holder for good. The
/// per-record forms (ProblemsOf) are the same rules for one definition, for one made at runtime.
/// </remarks>
internal static class ToggleContentValidation
{
    /// <exception cref="InvalidOperationException">A definition breaks a toggle rule, naming the definition and the rule.</exception>
    public static void EnsureValid(GameModuleContext context)
    {
        foreach (var action in context.Actions.Definitions)
        {
            ThrowFirst(ProblemsOf(action));
        }

        foreach (var item in context.Items.Definitions)
        {
            ThrowFirst(ProblemsOf(item));
        }
    }

    /// <summary>Every toggle rule action breaks, as a sentence naming it.</summary>
    public static IReadOnlyList<string> ProblemsOf(ActionDefinition action)
    {
        var heldBy = $"Action '{action.Name}'";
        var problems = new List<string>();

        // A toggle applies to whoever has it on, so there is no other target for it to resolve.
        if (action.Toggle is not null && !action.Activator.Targeting.IsSelfOnly)
        {
            problems.Add($"{heldBy} is a toggle but is not Self-targeted (a Self shape anchored on the caster): a toggle applies to its holder.");
        }

        AddUnrevertableHeldEffects(action, heldBy, problems);
        return problems;
    }

    /// <summary>Every toggle rule item breaks, as a sentence naming it.</summary>
    public static IReadOnlyList<string> ProblemsOf(ItemDefinition item)
    {
        var heldBy = $"Item '{item.Name}'";
        var problems = new List<string>();
        var hasToggleActivator = item.Activator is ToggleItemActivator;

        if (hasToggleActivator && item.Toggle is null)
        {
            problems.Add($"{heldBy} has a ToggleItemActivator but no ToggleSpec: an item is a toggle only with both.");
        }

        if (!hasToggleActivator && item.Toggle is not null)
        {
            problems.Add($"{heldBy} has a ToggleSpec but its activator is not a ToggleItemActivator: an item is a toggle only with both.");
        }

        AddUnrevertableHeldEffects(item, heldBy, problems);
        return problems;
    }

    /// <summary>A toggle's held effects may grant something with no end only through an entry that can take it back.</summary>
    private static void AddUnrevertableHeldEffects(ActivatableDefinition definition, string heldBy, List<string> problems)
    {
        if (definition.Toggle is not null)
        {
            EffectEntries.ForEach(definition.Effects, entry =>
            {
                if (entry.GrantsUntilRevoked && entry is not IReversibleEffectEntry)
                {
                    problems.Add($"{heldBy} is a toggle holding a {entry.GetType().Name} with no end, which can't be taken back when the toggle is switched off.");
                }
            });
        }
    }

    private static void ThrowFirst(IReadOnlyList<string> problems)
    {
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(problems[0]);
        }
    }
}
