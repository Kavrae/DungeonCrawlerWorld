using Engine.Utilities;

namespace Game.Modules.Health.Components;

/// <summary>Health tracking for an entity.</summary>
/// <param name="currentHealth">The entity's current health.</param>
/// <param name="maximumHealth">The entity's maximum health.</param>
/// <cleanupVersion>1</cleanupVersion>
public struct SimpleHealthComponent(float currentHealth, float maximumHealth)
{
    public float CurrentHealth { get; set; } = currentHealth;
    public float MaximumHealth { get; set; } = maximumHealth;

    public override readonly string ToString() =>
        MaximumHealth > 0
            ? $"{StringUtility.BuildPercentageBar("HP", (int)CurrentHealth, (int)MaximumHealth, 20)} {(int)CurrentHealth}/{(int)MaximumHealth}"
            : $"Invalid MaximumHealth: {MaximumHealth}";
}