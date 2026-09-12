using Engine.ECS.Systems;
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

    /// <summary>The simulation frame ComplexHealthRegenSystem may select this part again on, after it was disabled or burned. 0 means selectable.</summary>
    /// <remarks>
    /// The yo-yo-prevention lockout, a deadline rather than a countdown (PLAN-timer-wheel.md step
    /// 8): nothing advances it, and the one place that cares (BodyPartSelection.PickLowestPercentage)
    /// compares it against the current frame. That removed a whole-chain walk of every body part of
    /// every due entity from ComplexHealthRegenSystem's per-visit work, and with it the tier-cadence
    /// class of bug -- a lockout now ends when it should at any tier. Not on a timer wheel: nothing
    /// fires when it elapses, it simply stops excluding the part.
    /// </remarks>
    public uint RegenLockedUntilFrame { get; set; }

    /// <summary>True while this part is still inside its regen lockout as of now.</summary>
    public readonly bool IsRegenLockedOut(long now) => !FrameDeadline.IsReached(RegenLockedUntilFrame, now);

    public override readonly string ToString() =>
        MaximumHealth > 0
            ? $"{StringUtility.BuildPercentageBar(Name, (int)CurrentHealth, (int)MaximumHealth, 20)} {(int)CurrentHealth}/{(int)MaximumHealth}"
            : $"Invalid MaximumHealth: {MaximumHealth}";
}
