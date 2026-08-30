using Engine.Utilities;

namespace Game.Modules.Health.Components;

/// <summary>One tracked body part on a Complex-health entity.</summary>
/// <remarks>
/// Registered via ComponentManager.RegisterMultiPool -- no merge action, since Multi pools never
/// merge (Add always appends a new instance), which is exactly right here: two sources granting a
/// Goblin's own "Arm" part would be a bug to catch via testing, not silently averaged together the
/// way SimpleHealthComponent's Current/MaximumHealth merge today. An entity's health kind is never
/// a separate marker component -- it's whichever pool actually has entries for that entityId
/// (simpleHealth.Has vs bodyParts.Has), mirroring NonBlockingComponent.Kind folding its own
/// exemption-kind flag into the one component that grants the exemption rather than a second
/// component that could drift out of sync.
/// </remarks>
public struct BodyPartComponent(string name, BodyPartType type, byte partId, byte verticalPosition, float currentHealth, float maximumHealth, bool isVital)
{
    public string Name { get; set; } = name;
    public BodyPartType Type { get; set; } = type;

    /// <summary>Stable identity for this part, assigned once by ComplexHealthEffects.GrantBodyParts (sequential, in the granting race's own BodyParts list order) and permanent for the entity's lifetime -- unlike a MultiComponentPool dense index, which RemoveDenseIndexInternal can silently reassign to a different instance on removal elsewhere in the pool.</summary>
    public byte PartId { get; set; } = partId;

    /// <summary>Higher = higher up the body; meaningful only relative to this same entity's own other parts.</summary>
    public byte VerticalPosition { get; set; } = verticalPosition;
    public float CurrentHealth { get; set; } = currentHealth;
    public float MaximumHealth { get; set; } = maximumHealth;
    public bool IsVital { get; set; } = isVital;
    public bool IsDisabled { get; set; }

    /// <summary>Frames remaining before ComplexHealthRegenSystem may select this part again after it was disabled.</summary>
    /// <remarks>
    /// The yo-yo-prevention lockout, decremented directly by ComplexHealthRegenSystem's own
    /// per-visit walk, not CountdownTicker -- CountdownTicker is PackedComponentPool-only (see
    /// StatModifierExpirySystem's own doc comment for the same "not reusable here, this pool is
    /// Multi" reasoning), and a per-part field updated in place via UpdateByDenseIndex needs no
    /// separate ticking system regardless.
    /// </remarks>
    public ushort RegenLockoutFramesRemaining { get; set; }

    public override readonly string ToString() =>
        MaximumHealth > 0
            ? $"{StringUtility.BuildPercentageBar(Name, (int)CurrentHealth, (int)MaximumHealth, 20)} {(int)CurrentHealth}/{(int)MaximumHealth}"
            : $"Invalid MaximumHealth: {MaximumHealth}";
}
