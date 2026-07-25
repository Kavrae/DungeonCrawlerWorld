namespace Game.Modules.StatusEffectAura;

/// <summary>Status-effect aura's own rules: how often it grants a new batch of stacks, and the bounding radius for locating candidate entities near a moving source.</summary>
public static class AuraEffects
{
    /// <summary>
    /// How often an entity that remains in range gains another batch of stacks. A separate
    /// constant from BurningEffects.TickIntervalFrames -- even though both are currently 60 --
    /// since one is "how often the aura grants a new batch of stacks" and the other is "how
    /// often existing Burning stacks decay/damage"; independent knobs that only coincidentally
    /// match.
    /// </summary>
    public const int TickIntervalFrames = 60;

    /// <summary>
    /// Fixed scan radius used only for StatusEffectAuraSystem's rare "a source moved -- who
    /// nearby needs re-checking" query, not for per-mover detection (that's an O(1)
    /// AuraGrid.GetTotalStacksAt lookup). Must stay >= the largest
    /// DistanceFalloff.MaxRadius(Strength) of any StatusEffectAuraSourceComponent used
    /// in-game -- bump this if a stronger source is introduced. Lava uses Strength 8
    /// (MaxRadius 3), so 4 leaves headroom. A plain square bounding box is fine here even
    /// though the actual falloff shape is now a Manhattan diamond -- a square of the same
    /// radius is always a superset of the diamond, which is all a "who's roughly nearby"
    /// pre-filter needs.
    /// </summary>
    public const int MaxScanRadius = 4;
}
