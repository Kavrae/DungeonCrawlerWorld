using Engine.Utilities;

namespace Game.Modules.Health.Components;

/// <summary>Health tracking for an entity with one pool of health.</summary>
/// <remarks>
/// CurrentHealth already includes the entity's MaximumHealth modifiers (MaximumHealthShift moves it with them);
/// BaseMaximumHealth does not. Compare current health only with the modified maximum
/// (HealthQueries.TryGetTotals/TryGetEffectiveMaximum), never with BaseMaximumHealth.
/// </remarks>
/// <param name="currentHealth">The entity's current health.</param>
/// <param name="baseMaximumHealth">The entity's maximum health before any MaximumHealth modifier.</param>
/// <cleanupVersion>1</cleanupVersion>
public struct SimpleHealthComponent(float currentHealth, float baseMaximumHealth)
{
    public float CurrentHealth { get; set; } = currentHealth;
    public float BaseMaximumHealth { get; set; } = baseMaximumHealth;

    public override readonly string ToString() =>
        BaseMaximumHealth > 0
            ? $"{StringUtility.BuildPercentageBar("HP", (int)CurrentHealth, (int)BaseMaximumHealth, 20)} {(int)CurrentHealth}/{(int)BaseMaximumHealth} (base)"
            : $"Invalid BaseMaximumHealth: {BaseMaximumHealth}";
}
