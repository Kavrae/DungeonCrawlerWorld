namespace Game.Modules.ProcessingTier.Components;

/// <summary>
/// An entity's cached distance-from-player tier, recomputed once per ProcessingTierSystem's own
/// stripe cycle (so at most StripeCount frames stale). Throttling itself is driven by which
/// TieredEntityStripeSet bucket an entity lives in (see that class and ProcessingTierEvents),
/// not by consumers reading this component on every visit -- this is only read at the moment an
/// entity gains membership in some *other* system's population, to seed it into the right
/// starting bucket instead of defaulting to Local (see TieredEntityStripeSet.OnMemberAdded).
///
/// DirectComponentPool, not Packed: that membership-add lookup happens by entityId, and Direct
/// is a single entityId-indexed array read (same locality TransformComponent already gets)
/// versus Packed's two indirect hops (entityId -> denseIndex -> component). Costs one slot per
/// entity up to capacity rather than only entities that have a tier (a couple MB at this game's
/// scale) -- cheap relative to what it saves.
/// </summary>
public readonly record struct ProcessingTierComponent(ProcessingTierLevel Tier)
{
    public override readonly string ToString() => $"Tier : {Tier}";
}
