using Game.Effects;
using Game.Effects.Entries;

namespace Game.Modules.StatusEffects;

/// <summary>Checks that every status effect grant the build's content holds names a duration only for an effect that takes one, and never one of 0 ticks.</summary>
/// <remarks>
/// Run once every module is configured, so a grant whose duration would be silently ignored (Burning's stacks are its
/// duration, Paralysis's is fixed) fails the build instead. An effect no module registers is left alone, as
/// StatusEffectGrant skips it.
/// </remarks>
internal static class StatusEffectContentValidation
{
    /// <exception cref="InvalidOperationException">A definition's grant names a duration for an effect that takes none, naming both.</exception>
    public static void EnsureValid(GameModuleContext context)
    {
        var appliers = context.StatusEffectAppliers;
        EffectContent.ForEachEntry(context, (entry, heldBy) =>
        {
            foreach (var problem in ProblemsOf(appliers, entry, heldBy))
            {
                throw new InvalidOperationException(problem);
            }
        });
    }

    /// <summary>The problem with entry's duration, if any, as a sentence naming heldBy -- the per-record form, for a definition made at runtime.</summary>
    public static IEnumerable<string> ProblemsOf(StatusEffectApplierRegistry appliers, IEffectEntry entry, string heldBy)
    {
        if (entry is StatusEffectGrant { DurationTicks: 0 } zeroDurationGrant)
        {
            yield return $"{heldBy} grants {StatusEffectTypeFacts.DisplayNameOf(zeroDurationGrant.Type)} with a duration of 0 ticks, which would never start it.";
            yield break;
        }

        if (entry is StatusEffectGrant { DurationTicks: not null } grant
            && appliers.TryGet(grant.Type, out var applier)
            && !applier.TakesDuration)
        {
            yield return $"{heldBy} grants {StatusEffectTypeFacts.DisplayNameOf(grant.Type)} with a duration, but {StatusEffectTypeFacts.DisplayNameOf(grant.Type)} takes none.";
        }
    }
}
