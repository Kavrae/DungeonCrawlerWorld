namespace Engine.ECS.Systems;

/// <summary>
/// A system whose per-entity work is scheduled by a <see cref="TieredEntityStripeSet"/>. It hands
/// its stripe set to <see cref="SystemManager"/> and implements per-bucket work;
/// <see cref="TieredSystemRunner"/> owns the loop over tiers.
/// </summary>
/// <remarks>
/// <para>
/// Why the loop is not left to each system: every tiered system used to repeat the same
/// "for each tier, fetch the bucket and its frames-per-visit" loop, and the most common defect
/// found in this codebase was a system getting that loop subtly wrong -- decrementing a countdown
/// by a constant instead of <c>framesPerVisit</c>, or walking <c>GetDueEntities</c>, which cannot
/// expose <c>framesPerVisit</c> at all. With <c>framesPerVisit</c> handed to
/// <see cref="UpdateBucket"/> as a parameter, the correct value is always in front of the
/// implementer. That makes the right thing the default, not the only possibility: a system can
/// still ignore a parameter.
/// </para>
/// <para>
/// It is also where cross-cutting cadence policy lives. Which tiers are simulated at all is a
/// single value on <see cref="SystemManager.SimulatedTierCount"/>, injected by the game at bootstrap
/// -- this layer never learns what any tier means.
/// </para>
/// <para>
/// A tiered system still implements <see cref="ISystem.Update"/>, as a one-line call to
/// <see cref="TieredSystemRunner.Run"/> over every tier. That is what running a system standalone
/// (tests, chiefly) goes through. <see cref="SystemManager"/> does not call it: it calls
/// <see cref="TieredSystemRunner.Run"/> directly with its policy. So a tiered system must keep
/// <c>Update</c> to exactly that one line -- any other per-frame work belongs in
/// <see cref="BeginFrame"/>, or <see cref="SystemManager"/> will silently skip it.
/// </para>
/// <para>
/// Not every striped system fits. A system that runs two different tiered passes (StatusEffectAuraSystem)
/// or none at all (an event-driven system) stays a plain <see cref="ISystem"/> running its own loops.
/// </para>
/// </remarks>
public interface ITieredSystem : ISystem
{
    /// <summary>The stripe set that decides which entities are due each frame, and at which tier.</summary>
    TieredEntityStripeSet Tiers { get; }

    /// <summary>Once per frame, before any bucket. For work that is not per-entity -- draining a FrameEventBuffer, for example.</summary>
    void BeginFrame(EngineTime time)
    {
    }

    /// <summary>Once per simulated tier per frame, with that tier's due entities.</summary>
    /// <param name="time">The engine time.</param>
    /// <param name="entityIds">The entities due this frame at this tier. A view over the stripe set's own storage -- do not add or remove members of the driving pool while iterating it.</param>
    /// <param name="framesPerVisit">How many real frames each of these entities has been absent for, and so how much any per-entity countdown must be advanced by. Using a constant instead is the defect this interface exists to make harder.</param>
    void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit);
}
