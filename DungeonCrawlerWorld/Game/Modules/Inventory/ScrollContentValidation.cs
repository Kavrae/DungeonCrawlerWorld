using Game.Modules.Actions.Activators;

namespace Game.Modules.Inventory;

/// <summary>Checks that every scroll binds something a scroll can cast: a spell that isn't a toggle.</summary>
/// <remarks>
/// A scroll casts its spell once and is used up (ScrollActivator), so what it binds must be a spell (a SpellActivator
/// action) and must not be a toggle, which a used-up item could never switch off. ProblemsOf is the same rule for one
/// definition, for one made at runtime. Run beside ItemCooldownContentValidation, so a mod breaking it fails its dry run.
/// </remarks>
internal static class ScrollContentValidation
{
    /// <exception cref="InvalidOperationException">A scroll binds something it can't cast, naming the scroll.</exception>
    public static void EnsureValid(GameModuleContext context)
    {
        foreach (var item in context.Items.Definitions)
        {
            var problems = ProblemsOf(item);
            if (problems.Count > 0)
            {
                throw new InvalidOperationException(problems[0]);
            }
        }
    }

    /// <summary>Every scroll rule item breaks, as a sentence naming it; none for an item that isn't a scroll.</summary>
    public static IReadOnlyList<string> ProblemsOf(ItemDefinition item)
    {
        if (item.Activator is not ScrollActivator scroll)
        {
            return [];
        }

        var problems = new List<string>();
        if (scroll.Spell.Activator is not SpellActivator)
        {
            problems.Add($"Item '{item.Name}' is a scroll of '{scroll.Spell.Name}', which isn't a spell: a scroll binds an action with a Spell activator.");
        }

        if (scroll.Spell.Toggle is not null)
        {
            problems.Add($"Item '{item.Name}' is a scroll of '{scroll.Spell.Name}', a toggle: a scroll is used up when it is read, so nothing could switch it off.");
        }

        return problems;
    }
}
