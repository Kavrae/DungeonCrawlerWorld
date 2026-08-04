namespace Game.Modules.ProcessingTier.Components;

/// <summary>Each ProcessingTierLevel's stripe-cadence multiplier -- index order matches the enum's own declared order (Local, Neighborhood, Borough, Beyond = 0-3). The one place this mapping lives; every TieredEntityStripeSet consumer references this directly rather than declaring its own copy.</summary>
public static class ProcessingTierDivisors
{
    public static readonly byte[] ByTierIndex = [1, 2, 4, 8];
}
