using Game.Modules.Actions.Effects;

namespace Game.Modules.Actions;

/// <summary>What makes an action or item a dodge -- what it does, never which Guid it has -- and the rule a dodge's step keeps.</summary>
/// <remarks>
/// A dodge is anything whose own effects hold a DodgeActivation: today's Dodge, an enhanced dodge an NPC starts with,
/// one the player learns, a race's override of Dodge. Presentation asks this of the armed action's effective definition
/// (ActionStateView.TryGetEffectiveAction) to offer the dodge's step tiles and WASD confirm. A dodge steps one tile: its
/// targeting's MaximumRange is its step, and a longer step isn't built yet (DodgeFootprint), so a dodge declaring one is
/// refused rather than silently stepping less than it says. ProblemsOf is that rule for one definition; EnsureValid runs
/// it over the build's content beside ToggleContentValidation.
/// </remarks>
public static class Dodges
{
    /// <summary>How far a dodge steps, in tiles: the only step length built.</summary>
    public const int StepTiles = 1;

    /// <summary>Whether definition is a dodge: one of its own effects' entries is a DodgeActivation.</summary>
    public static bool IsDodge(ActivatableDefinition definition)
    {
        var effects = definition.Effects;
        for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            var entries = effects[effectIndex].Entries;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                if (entries[entryIndex] is DodgeActivation)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The dodge rule definition breaks, as a sentence naming heldBy; none for one that isn't a dodge or steps one tile.</summary>
    public static IReadOnlyList<string> ProblemsOf(ActivatableDefinition definition, string heldBy) =>
        IsDodge(definition) && definition.ActivatorOrNone is { } activator && activator.Targeting.MaximumRange > StepTiles
            ? [$"{heldBy} is a dodge with a range of {activator.Targeting.MaximumRange}: a dodge steps {StepTiles} tile, and longer steps aren't built yet. Set its range to {StepTiles}."]
            : [];

    /// <exception cref="InvalidOperationException">A dodge declares a step longer than one tile, naming it.</exception>
    internal static void EnsureValid(GameModuleContext context)
    {
        foreach (var action in context.Actions.Definitions)
        {
            ThrowFirst(ProblemsOf(action, $"Action '{action.Name}'"));
        }

        foreach (var item in context.Items.Definitions)
        {
            ThrowFirst(ProblemsOf(item, $"Item '{item.Name}'"));
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
