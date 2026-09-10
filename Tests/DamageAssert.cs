using Game.Modules.Actions;

namespace Tests;

/// <summary>
/// Assertions for a value produced by DirectDamage, which always rolls a crit and so has two
/// legitimate outcomes rather than one.
/// </summary>
/// <remarks>
/// DirectDamage.Apply ends with an unconditional crit roll against CritMath.BaseCritChance (5%),
/// multiplying the fully-scaled result by CritMath.BaseCritMultiplier (3x) on success. Any test
/// that asserts an exact post-damage number is therefore asserting only one of two correct
/// answers, and fails on the roll it didn't expect. That is a real defect this codebase shipped:
/// ConsumableActivationSystemTests' wand tests deal a flat 10 to a 20-health target and assert 10
/// remaining, so a crit took the target to 0 instead and the test failed roughly one suite run in
/// ten -- diagnosed only after it had been dismissed as unexplained flakiness more than once.
///
/// The alternative fix, and the one three fixtures had already reached for independently, is a
/// Random subclass pinning NextDouble to 1.0 so the crit can never fire. That works but hides the
/// mechanic rather than accounting for it: the test passes because crits were switched off, not
/// because it tolerates them, and it silently stops being true the moment the fixture's randomizer
/// changes. Accepting either outcome keeps the assertion honest under any randomizer, including
/// the live one the game itself now runs seeded (see RandomSeed).
///
/// The trade-off is deliberate and worth stating: an either/or assertion is weaker than an exact
/// one, and would not catch a change that made damage exactly triple. That specific case is what
/// DirectDamageTests' own AlwaysCritRandom tests are for -- crit is asserted exactly, in the tests
/// that exist to assert it, and merely tolerated everywhere else.
/// </remarks>
internal static class DamageAssert
{
    /// <summary>
    /// Asserts health landed on either the un-crit or the crit outcome of a single DirectDamage
    /// application of expectedNormalDamage.
    /// </summary>
    /// <param name="startingHealth">The target's health before the damage was applied.</param>
    /// <param name="expectedNormalDamage">The damage the effect deals on a non-crit roll.</param>
    /// <param name="actualHealth">The target's health after the damage was applied.</param>
    /// <param name="message">Optional context appended to the failure message.</param>
    public static void HealthAfterDamage(float startingHealth, float expectedNormalDamage, float actualHealth, string? message = null)
    {
        // Both outcomes clamp at zero, matching HealthDamage.Apply -- a crit that overkills leaves
        // the target at 0, not negative, so the crit expectation has to clamp too or it can never
        // be matched on a low-health target (exactly the wand case above: 20 health, 30 crit).
        var normalHealth = System.Math.Max(0f, startingHealth - expectedNormalDamage);
        var critHealth = System.Math.Max(0f, startingHealth - expectedNormalDamage * CritMath.BaseCritMultiplier);

        if (actualHealth == normalHealth || actualHealth == critHealth)
        {
            return;
        }

        Assert.Fail(
            $"Expected {normalHealth} (no crit) or {critHealth} (crit, {expectedNormalDamage} x {CritMath.BaseCritMultiplier}), but was {actualHealth}." +
            (message is null ? string.Empty : $" {message}"));
    }

    /// <summary>Asserts damage dealt was either the un-crit or the crit amount -- the same rule as HealthAfterDamage, for a test that measures the damage itself rather than the health left behind.</summary>
    /// <param name="expectedNormalDamage">The damage the effect deals on a non-crit roll.</param>
    /// <param name="actualDamage">The damage actually dealt.</param>
    /// <param name="message">Optional context appended to the failure message.</param>
    public static void DamageDealt(float expectedNormalDamage, float actualDamage, string? message = null)
    {
        if (actualDamage == expectedNormalDamage || actualDamage == expectedNormalDamage * CritMath.BaseCritMultiplier)
        {
            return;
        }

        Assert.Fail(
            $"Expected {expectedNormalDamage} (no crit) or {expectedNormalDamage * CritMath.BaseCritMultiplier} (crit), but was {actualDamage}." +
            (message is null ? string.Empty : $" {message}"));
    }
}
