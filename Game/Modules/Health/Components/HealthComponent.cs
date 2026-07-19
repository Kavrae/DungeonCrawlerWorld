using Engine.Utilities;

namespace Game.Modules.Health.Components;

/// <summary>
/// An entity's health bounds and passive regeneration. Contains only base health
/// statistics; additional health types should be separate components.
/// </summary>
public struct HealthComponent(short currentHealth, short healthRegen, short maximumHealth)
{
    public short CurrentHealth { get; set; } = currentHealth;
    public short HealthRegen { get; set; } = healthRegen;
    public short MaximumHealth { get; set; } = maximumHealth;

    // BuildPercentageBar throws for maximumValue <= 0 (a caller bug, by its own contract) --
    // guarded here rather than there, since a stray/default-valued component must not crash
    // the debug inspector that calls ToString() on whatever an entity actually has.
    public override readonly string ToString() =>
        MaximumHealth > 0
            ? StringUtility.BuildPercentageBar("HP", CurrentHealth, MaximumHealth, 20)
            : $"HP : [invalid MaximumHealth: {MaximumHealth}]";
}
