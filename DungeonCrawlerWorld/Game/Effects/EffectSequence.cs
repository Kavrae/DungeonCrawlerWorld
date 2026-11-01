using Engine.Math;

namespace Game.Effects;

/// <summary>Applies an ordered list of Effect, in list order.</summary>
/// <remarks>The one loop everything that applies effects shares: an action's or item's activation (split by placement into ApplyOnEachTarget, ApplyOnce and ApplyOnEachTile), and everything that applies to a single entity (an aura's tick, a terrain contact, ChainedEffect's triggered effects).</remarks>
public static class EffectSequence
{
    public static EffectOutcome Apply(IReadOnlyList<Effect> effects, in EffectContext context)
    {
        var outcome = EffectOutcome.NoEffect;
        for (var index = 0; index < effects.Count; index++)
        {
            outcome = EffectOutcomes.Combine(outcome, effects[index].Apply(in context));
        }

        return outcome;
    }

    /// <summary>Applies the OnEachTarget entries of effects to context.TargetEntityId, in list order, skipping the entries placed once (ApplyOnce).</summary>
    public static EffectOutcome ApplyOnEachTarget(IReadOnlyList<Effect> effects, in EffectContext context)
    {
        var outcome = EffectOutcome.NoEffect;
        for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            var entries = effects[effectIndex].Entries;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                if (entry.Placement == EffectPlacement.OnEachTarget)
                {
                    outcome = EffectOutcomes.Combine(outcome, entry.Apply(in context));
                }
            }
        }

        return outcome;
    }

    /// <summary>Applies the entries of effects placed once per activation, in list order: OncePerActivation on markedEntityId, or at centre with no target entity when nothing is marked; AtLocation at centre with no target entity.</summary>
    /// <remarks>context's target is replaced; its source, name, tags and frame are kept.</remarks>
    public static EffectOutcome ApplyOnce(IReadOnlyList<Effect> effects, in EffectContext context, int? markedEntityId, Vector3Int centre)
    {
        var outcome = EffectOutcome.NoEffect;
        for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            var entries = effects[effectIndex].Entries;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                var targetEntityId = entry.Placement switch
                {
                    EffectPlacement.OncePerActivation => markedEntityId ?? EffectContext.NoTargetEntity,
                    EffectPlacement.AtLocation => EffectContext.NoTargetEntity,
                    _ => (int?)null,
                };

                if (targetEntityId is { } target)
                {
                    outcome = EffectOutcomes.Combine(outcome, entry.Apply(context with { TargetEntityId = target, TargetLocation = centre }));
                }
            }
        }

        return outcome;
    }

    /// <summary>Applies the AtLocation entries of effects at centre with no target entity, in list order: what ApplyOnce places when the marked entity was missed (it dodged, or isn't simulated), whose OncePerActivation entries land nowhere.</summary>
    /// <remarks>context's target is replaced; its source, name, tags and frame are kept.</remarks>
    public static EffectOutcome ApplyAtLocation(IReadOnlyList<Effect> effects, in EffectContext context, Vector3Int centre)
    {
        var outcome = EffectOutcome.NoEffect;
        for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            var entries = effects[effectIndex].Entries;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                if (entry.Placement == EffectPlacement.AtLocation)
                {
                    outcome = EffectOutcomes.Combine(outcome, entry.Apply(context with { TargetEntityId = EffectContext.NoTargetEntity, TargetLocation = centre }));
                }
            }
        }

        return outcome;
    }

    /// <summary>Applies the OnEachTile entries of effects at each of tiles with no target entity, tile by tile, each tile's in list order.</summary>
    /// <remarks>context's target is replaced; its source, name, tags and frame are kept.</remarks>
    public static EffectOutcome ApplyOnEachTile(IReadOnlyList<Effect> effects, in EffectContext context, IReadOnlyList<Vector3Int> tiles)
    {
        var outcome = EffectOutcome.NoEffect;
        if (!HoldsPlacement(effects, EffectPlacement.OnEachTile))
        {
            return outcome;
        }

        for (var tileIndex = 0; tileIndex < tiles.Count; tileIndex++)
        {
            var tileContext = context with { TargetEntityId = EffectContext.NoTargetEntity, TargetLocation = tiles[tileIndex] };
            for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                var entries = effects[effectIndex].Entries;
                for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    var entry = entries[entryIndex];
                    if (entry.Placement == EffectPlacement.OnEachTile)
                    {
                        outcome = EffectOutcomes.Combine(outcome, entry.Apply(in tileContext));
                    }
                }
            }
        }

        return outcome;
    }

    /// <summary>Whether any entry effects holds directly is placed by placement.</summary>
    private static bool HoldsPlacement(IReadOnlyList<Effect> effects, EffectPlacement placement)
    {
        for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            var entries = effects[effectIndex].Entries;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                if (entries[entryIndex].Placement == placement)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The first reason an entry effects holds can't be applied to context.TargetEntityId now (IEffectEntry.CanApply), or None when all of them can. None for no effects.</summary>
    /// <remarks>
    /// The entries are asked together, in list order, against one EffectReservations: each is asked against
    /// what the ones before it left. Asks the entries the effects hold directly, not the ones those apply
    /// in turn: a ChainedEffect answers for itself.
    /// </remarks>
    public static EffectRefusal CanApply(IReadOnlyList<Effect> effects, in EffectContext context)
    {
        var reservations = new EffectReservations();
        for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            var entries = effects[effectIndex].Entries;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var refusal = entries[entryIndex].CanApply(in context, ref reservations);
                if (refusal != EffectRefusal.None)
                {
                    return refusal;
                }
            }
        }

        return EffectRefusal.None;
    }

    /// <summary>Reverts every reversible entry effects holds, nested ones included, under context.HeldGrantKey.</summary>
    public static void Revert(IReadOnlyList<Effect> effects, in EffectContext context)
    {
        for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++)
        {
            var entries = effects[effectIndex].Entries;
            for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
            {
                var entry = entries[entryIndex];
                if (entry is IReversibleEffectEntry reversible)
                {
                    reversible.Revert(in context);
                }

                Revert(entry.NestedEffects, in context);
            }
        }
    }
}
