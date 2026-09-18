namespace Game.Modules.ProcessingTier.Components;

/// <summary>Each ProcessingTierLevel's stripe-cadence multiplier -- index order matches the enum's own declared order (Local, Neighborhood, Borough, Beyond = 0-3) -- plus how many of those tiers are simulated at all. The one place this policy lives; every TieredEntityStripeSet consumer and GameBootstrapper reference it directly.</summary>
/// <remarks>
/// Local runs every visit, Neighborhood at 1/8 speed, and Borough and
/// Beyond are not simulated (SimulatedTierCount = 2). Their divisors are never used while that
/// holds; they only keep the array's length equal to the tier count.
///
/// A divisor coarsens how often an entity is visited, never how fast time passes for it: a tiered
/// system is handed framesPerVisit and scales anything it accumulates by it (CLAUDE.md).
///
/// ushort headroom, not byte: the largest base StripeCount in use is 60 (GameTiming.FramesPerSecond),
/// so a bucket can reach 60 * 64 = 3,840 -- see EntityStripeSet.StripeCount.
/// </remarks>
public static class ProcessingTierDivisors
{
    public static readonly byte[] ByTierIndex = [1, 8, 16, 64];

    /// <summary>How many tiers, from Local up, are simulated -- SystemManager.SimulatedTierCount, and the SimulationScope policy timer wheels consult.</summary>
    public const int SimulatedTierCount = 2;
}
