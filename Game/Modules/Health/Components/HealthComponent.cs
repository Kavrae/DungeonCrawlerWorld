using Engine.Utilities;

namespace Game.Modules.Health.Components;

/// <summary>
/// An entity's health bounds. Contains only base health statistics; additional health types
/// should be separate components. Regen is not stored here -- HealthRegenSystem computes it live
/// each tick from the entity's Constitution AbilityScoreComponent.Total (see
/// AbilityScoreRegenMath), so there's no cached rate to keep in sync when Constitution changes.
///
/// float, not short: regen adds a fractional amount every tick (e.g. 2%/sec of a 100-point pool
/// is 2.0, but of a 6-point pool is 0.12), and storing the exact value here means regen never has
/// to round at all -- see HealthRegenSystem's own doc comment for what rounding this away used to
/// cost (stochastic rounding's occasional multi-tick dry streak was a real, reported UX bug at
/// low pool sizes).
/// </summary>
public struct HealthComponent(float currentHealth, float maximumHealth)
{
    public float CurrentHealth { get; set; } = currentHealth;
    public float MaximumHealth { get; set; } = maximumHealth;

    // BuildPercentageBar throws for maximumValue <= 0 (a caller bug, by its own contract) --
    // guarded here rather than there, since a stray/default-valued component must not crash
    // the debug inspector that calls ToString() on whatever an entity actually has. Truncated to
    // int for both the bar and the display text -- the fractional part is real (see this
    // struct's own doc comment) but not something a player needs to see down to the decimal.
    public override readonly string ToString() =>
        MaximumHealth > 0
            ? $"{StringUtility.BuildPercentageBar("HP", (int)CurrentHealth, (int)MaximumHealth, 20)} {(int)CurrentHealth}/{(int)MaximumHealth}"
            : $"HP : [invalid MaximumHealth: {MaximumHealth}]";
}