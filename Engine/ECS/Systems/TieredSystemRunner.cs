namespace Engine.ECS.Systems;

/// <summary>
/// The one implementation of the "for each tier, run its due bucket" loop. Used by
/// <see cref="SystemManager"/> (with its tier policy) and by every <see cref="ITieredSystem"/>'s own
/// <see cref="ISystem.Update"/> (over every tier), so the two cannot drift apart. Drift of exactly
/// that kind already happened once: PoisonSystemTests' own frame helper stayed correct only while
/// the system's StripeCount happened to be 1.
/// </summary>
public static class TieredSystemRunner
{
    /// <summary>Runs one frame of system: BeginFrame, then UpdateBucket for each simulated tier in ascending order.</summary>
    /// <param name="system">The system to run.</param>
    /// <param name="time">The engine time. Its FrameCount selects each tier's due bucket.</param>
    /// <param name="simulatedTierCount">How many tiers, from the lowest index up, are simulated. Tiers at or past this are skipped entirely -- no visit, no countdown. Clamped to the stripe set's own tier count, so int.MaxValue means "all".</param>
    public static void Run(ITieredSystem system, EngineTime time, int simulatedTierCount = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(system);

        system.BeginFrame(time);

        var tiers = system.Tiers;
        var tierCount = System.Math.Min(simulatedTierCount, tiers.TierCount);

        for (var tierIndex = 0; tierIndex < tierCount; tierIndex++)
        {
            system.UpdateBucket(time, tiers.GetTierBucket(tierIndex, time.FrameCount), tiers.GetTierFramesPerVisit(tierIndex));
        }
    }
}
