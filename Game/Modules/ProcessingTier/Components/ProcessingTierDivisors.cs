namespace Game.Modules.ProcessingTier.Components;

/// <summary>Each ProcessingTierLevel's stripe-cadence multiplier -- index order matches the enum's own declared order (Local, Neighborhood, Borough, Beyond = 0-3). The one place this mapping lives; every TieredEntityStripeSet consumer references this directly rather than declaring its own copy.</summary>
/// <remarks>
/// Was [1, 2, 4, 8]. Raised because that spread was far shallower than the population it
/// throttles, and because the tier that holds the bulk of the population is Neighborhood, not
/// Beyond -- ProcessingTierSystem's NeighborhoodSizeTiles is 1000 against a 1000x1000 map, so
/// "same 1000-tile grid cell as the player" means the entire rest of the player's own layer.
/// (Borough needs a position outside that cell but inside the 2000-tile one, which cannot exist
/// at the current map size, so that tier is empty today -- but only today: the map is planned to
/// grow to Borough size and then to 4x Borough, at which point Borough and then Beyond hold the
/// bulk of the population instead of Neighborhood. These values are chosen to be right at those
/// sizes too, not just this one.) Local holds on the order of a thousand entities; the
/// Neighborhood tier behind it holds tens of thousands, and was being visited at half Local's
/// cadence.
///
/// What a divisor actually costs, now that the tiered systems compensate correctly for it (see
/// ActionLockSystem.Update's remarks): an entity is visited every StripeCount * divisor frames
/// and has that full span deducted from its countdowns in one go, so it becomes ready to act on
/// each visit rather than needing several. Raising the divisor therefore does not desynchronise
/// countdowns -- it coarsens how often a distant entity can act, in exchange for a proportional
/// drop in visit cost.
///
/// Safe against the viewport at the only zoom that matters: Local's radius is 80 tiles and a
/// Team-zoom viewport is roughly 23 tiles from centre, so nothing at Neighborhood or beyond is
/// ever on screen. Tier recomputation is itself striped (ProcessingTierSystem, base 15), so a
/// Neighborhood entity's tier is re-evaluated every 15 * 16 = 240 frames; an entity closing on
/// the player still has ~57 tiles of margin between the Local boundary and the viewport edge to
/// be promoted in, far more than four seconds of movement.
///
/// ushort headroom, not byte: the largest base StripeCount in use is 60
/// (GameTiming.FramesPerSecond), so the widest bucket is 60 * 64 = 3,840 -- see
/// EntityStripeSet.StripeCount for why that had to stop being a byte before these could be
/// raised at all.
/// </remarks>
public static class ProcessingTierDivisors
{
    public static readonly byte[] ByTierIndex = [1, 16, 32, 64];
}
