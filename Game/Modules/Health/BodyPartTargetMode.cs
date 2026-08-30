namespace Game.Modules.Health;

/// <summary>
/// How many of a Complex-health entity's body parts a damage/heal effect affects -- shared by
/// DirectDamage and DirectHeal (see each one's own doc comment) rather than each growing its own
/// bespoke targeting concept.
/// </summary>
public enum BodyPartTargetMode : byte
{
    /// <summary>Exactly one part: random, or a specific BodyPartType with fallback (see BodyPartTargetRule) -- today's only damage behavior, and (with a BodyPartTargetRule) how ContactDamageSystem/MagicMissileAction already work.</summary>
    SingleTarget,

    /// <summary>Exactly one part: whichever has the lowest current/effective-maximum health fraction (BodyPartSelection.PickLowestPercentage) -- previously only reachable by passive regen.</summary>
    LowestPercentage,

    /// <summary>Every part the entity owns. The full amount (after every other modifier has already been applied) is computed once and split evenly across however many parts exist, rather than recomputed per part -- see ComplexHealthDamage.ApplyToAllParts/ComplexHealthHeal's own All-mode doc comments for why: a flat component must not be multiplied by body-part count.</summary>
    All,
}
