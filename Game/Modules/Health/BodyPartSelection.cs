using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Game.Modules.Health;

/// <summary>Selection rules for picking one of a Complex entity's BodyPartComponent instances.</summary>
/// <remarks>
/// Shared by every system/helper that needs to pick a part, rather than each re-walking entityId's
/// chain its own way. PickRandom/PickByType/PickTopmost/PickBottommost all prefer a non-disabled
/// part, falling back to any part (including a disabled one) only when every part is disabled --
/// the defensive same-frame edge case where an entity's last Vital part just hit 0 but death
/// processing hasn't removed it yet, since the entity should otherwise already be dead.
/// </remarks>
public static class BodyPartSelection
{
    /// <summary>Picks one of entityId's non-disabled body parts uniformly at random, falling back to any part (including a disabled one) if every part is currently disabled.</summary>
    /// <remarks>
    /// The "attacks hit a random body part (for now)" placeholder TODO.md's Body parts item names,
    /// until the Targeted body part damage follow-up adds real selection rules. Two-pass walk
    /// (count, then walk to the Nth) since MultiComponentPool exposes no direct "the Nth instance
    /// for this entity" accessor. Returns -1 if entityId owns no BodyPartComponent at all.
    /// </remarks>
    public static int PickRandom(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, MathUtility mathUtility)
    {
        var aliveDenseIndex = PickRandomFiltered(bodyParts, entityId, mathUtility, aliveOnly: true);
        return aliveDenseIndex != -1
            ? aliveDenseIndex
            : PickRandomFiltered(bodyParts, entityId, mathUtility, aliveOnly: false);
    }

    /// <summary>Shared two-pass walk behind PickRandom's alive-preferring and any-part fallback behavior.</summary>
    private static int PickRandomFiltered(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, MathUtility mathUtility, bool aliveOnly)
    {
        var count = 0;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            if (!aliveOnly || !bodyParts.GetReadonlyByDenseIndex(denseIndex).IsDisabled)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return -1;
        }

        var targetOrdinal = mathUtility.Next(0, count);
        var ordinal = 0;
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            if (aliveOnly && bodyParts.GetReadonlyByDenseIndex(denseIndex).IsDisabled)
            {
                continue;
            }

            if (ordinal == targetOrdinal)
            {
                return denseIndex;
            }

            ordinal++;
        }

        return -1; // Unreachable given count > 0, guarded for completeness.
    }

    /// <summary>Picks entityId's body part with the lowest CurrentHealth/effective-MaximumHealth fraction, skipping any part still inside its post-disable lockout window or currently burning (bodyPartBurningTimers).</summary>
    /// <remarks>
    /// The yo-yo-prevention case RegenLockoutFramesRemaining exists for. Its only caller is
    /// ComplexHealthRegenSystem's own passive-regen tick -- an active heal (potion/scroll) never
    /// goes through this method at all, see ComplexHealthHeal.ApplyFractionToAllParts, which heals
    /// every part at once rather than picking one, so there is no "should this ignore the lockout"
    /// question for the heal path to begin with. Fraction is computed against each part's own
    /// modifier-effective maximum (StatModifierMath, same chain ComplexHealthDamage/
    /// ComplexHealthRegenSystem's own clamp already uses), not the raw MaximumHealth field -- a
    /// part sitting at 100% of its raw max with an active MaximumHealth buff still has real
    /// headroom up to the effective one, and treating it as "already full" here would leave it
    /// permanently unselectable, stuck below the true cap regen should still be closing.
    /// bodyPartBurningTimers is a second, independent exclusion from the lockout timer -- a part
    /// actively on fire must never regen even once its numeric lockout has counted down to 0, since
    /// "on fire" is its own condition, not just a longer lockout (see PLAN-per-body-part-status-effects.md).
    /// Returns -1 if entityId owns no BodyPartComponent, or every part is either at its effective
    /// maximum, locked out, or currently burning.
    /// </remarks>
    public static int PickLowestPercentage(
        MultiComponentPool<BodyPartComponent> bodyParts,
        int entityId,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers = null)
    {
        var bestDenseIndex = -1;
        var bestFraction = float.MaxValue;

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (part.RegenLockoutFramesRemaining > 0)
            {
                continue;
            }

            if (IsCurrentlyBurning(bodyPartBurningTimers, entityId, part.PartId))
            {
                continue;
            }

            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, entityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            var fraction = effectiveMaximumHealth > 0 ? part.CurrentHealth / effectiveMaximumHealth : 1f;
            if (fraction >= 1f)
            {
                continue; // Already at its effective maximum, nothing to gain by selecting it.
            }

            if (fraction < bestFraction)
            {
                bestFraction = fraction;
                bestDenseIndex = denseIndex;
            }
        }

        return bestDenseIndex;
    }

    /// <summary>True if entityId has an active BodyPartBurningTimerComponent entry for partId -- a short linear walk of the entity's own, typically very small, burning-parts chain.</summary>
    private static bool IsCurrentlyBurning(MultiComponentPool<BodyPartBurningTimerComponent>? bodyPartBurningTimers, int entityId, byte partId)
    {
        if (bodyPartBurningTimers is null)
        {
            return false;
        }

        for (var denseIndex = bodyPartBurningTimers.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyPartBurningTimers.GetNextDenseIndex(denseIndex))
        {
            if (bodyPartBurningTimers.GetReadonlyByDenseIndex(denseIndex).PartId == partId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Picks entityId's highest-VerticalPosition non-disabled body part (e.g. the Head), falling back to the highest overall if every part is disabled. preferAlive: false skips straight to the disabled-inclusive pass, for a caller that needs a deterministic, disabled-status-independent pick instead (see BurningAuraApplier's own doc comment for why).</summary>
    /// <remarks>Returns -1 if entityId owns no BodyPartComponent.</remarks>
    public static int PickTopmost(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, bool preferAlive = true)
    {
        if (preferAlive)
        {
            var aliveDenseIndex = PickExtreme(bodyParts, entityId, preferHigher: true, aliveOnly: true);
            if (aliveDenseIndex != -1)
            {
                return aliveDenseIndex;
            }
        }

        return PickExtreme(bodyParts, entityId, preferHigher: true, aliveOnly: false);
    }

    /// <summary>Picks entityId's lowest-VerticalPosition non-disabled body part (e.g. a Foot), falling back to the lowest overall if every part is disabled. preferAlive: false skips straight to the disabled-inclusive pass, for a caller that needs a deterministic, disabled-status-independent pick instead (see BurningAuraApplier's own doc comment for why).</summary>
    /// <remarks>Returns -1 if entityId owns no BodyPartComponent.</remarks>
    public static int PickBottommost(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, bool preferAlive = true)
    {
        if (preferAlive)
        {
            var aliveDenseIndex = PickExtreme(bodyParts, entityId, preferHigher: false, aliveOnly: true);
            if (aliveDenseIndex != -1)
            {
                return aliveDenseIndex;
            }
        }

        return PickExtreme(bodyParts, entityId, preferHigher: false, aliveOnly: false);
    }

    /// <summary>Shared linear walk behind PickTopmost/PickBottommost, parameterized by comparison direction and by whether disabled parts are excluded from consideration.</summary>
    private static int PickExtreme(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, bool preferHigher, bool aliveOnly)
    {
        var bestDenseIndex = -1;
        var bestPosition = preferHigher ? -1 : byte.MaxValue + 1;

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (aliveOnly && part.IsDisabled)
            {
                continue;
            }

            if (preferHigher ? part.VerticalPosition > bestPosition : part.VerticalPosition < bestPosition)
            {
                bestPosition = part.VerticalPosition;
                bestDenseIndex = denseIndex;
            }
        }

        return bestDenseIndex;
    }

    /// <summary>Picks entityId's first non-disabled body part of the requested type, falling back to a disabled part of that type if no alive one exists. preferAlive: false returns the first match outright regardless of disabled status, for a caller that needs a deterministic, disabled-status-independent pick instead (see BurningAuraApplier's own doc comment for why).</summary>
    /// <remarks>Returns -1 if entityId owns no BodyPartComponent of that type at all -- the expected "no Foot on this race" outcome, not an error case.</remarks>
    public static int PickByType(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, BodyPartType type, bool preferAlive = true)
    {
        var disabledMatchDenseIndex = -1;

        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            if (part.Type != type)
            {
                continue;
            }

            if (!preferAlive || !part.IsDisabled)
            {
                return denseIndex;
            }

            if (disabledMatchDenseIndex == -1)
            {
                disabledMatchDenseIndex = denseIndex;
            }
        }

        return disabledMatchDenseIndex;
    }

    /// <summary>
    /// Picks entityId's body part matching rule.PreferredType, falling back to rule.Fallback's own
    /// selection when no part of that type exists (or rule.PreferredType is null -- no type
    /// preference at all, e.g. lava's generic bottom-up targeting).
    /// </summary>
    /// <remarks>
    /// preferAlive: false makes the whole resolution deterministic and disabled-status-independent
    /// -- the same rule always maps to the same part, whether or not that part is currently
    /// disabled. BurningAuraApplier is the one caller that needs this: it must keep re-resolving to
    /// the *same* part on every aura re-grant tick so it keeps topping off the one existing timer
    /// instead of drifting to a different part once the original target hits 0 (see its own doc
    /// comment) -- reusing the ordinary alive-preferring pick (the default here, used by every other
    /// caller -- ComplexHealthDamage/ContactDamageSystem's own fresh, one-off hit resolutions, which
    /// *should* keep steering away from an already-destroyed part) would silently break that
    /// stability the instant the target part became disabled.
    /// </remarks>
    public static int PickByTypeWithFallback(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, BodyPartTargetRule rule, MathUtility mathUtility, bool preferAlive = true)
    {
        var typeMatch = rule.PreferredType is { } type ? PickByType(bodyParts, entityId, type, preferAlive) : -1;
        if (typeMatch != -1)
        {
            return typeMatch;
        }

        return rule.Fallback switch
        {
            BodyPartFallback.Topmost => PickTopmost(bodyParts, entityId, preferAlive),
            BodyPartFallback.Bottommost => PickBottommost(bodyParts, entityId, preferAlive),
            _ => PickRandom(bodyParts, entityId, mathUtility),
        };
    }

    /// <summary>Finds entityId's body part with the given stable PartId.</summary>
    /// <remarks>Mirrors PickByType's linear-walk shape, matching PartId instead of Type -- unlike a dense index, PartId is stable across removals elsewhere in the pool, so this is the correct way to re-locate a specific, previously-known part (e.g. BodyPartBurningSystem re-finding the exact part its own timer names).</remarks>
    public static int FindByPartId(MultiComponentPool<BodyPartComponent> bodyParts, int entityId, byte partId)
    {
        for (var denseIndex = bodyParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            if (bodyParts.GetReadonlyByDenseIndex(denseIndex).PartId == partId)
            {
                return denseIndex;
            }
        }

        return -1;
    }
}
