namespace Engine.ECS.Systems;

/// <summary>
/// A system acts on one or more components via Update(). Drawing is handled entirely by
/// Presentation, never here.
/// </summary>
public interface ISystem
{
    /// <summary>
    /// Called every frame by SystemManager, which also owns and advances the rotating
    /// stripeIndex (0..StripeCount-1) -- centralized there instead of each system tracking
    /// its own cursor, since the increment-and-wrap bookkeeping is identical for every
    /// system regardless of what it does with the stripe. Implementations should process
    /// only the entities assigned to stripeIndex (see EntityStripeSet) rather than gating
    /// the whole population on a period -- this keeps per-frame cost proportional to
    /// Count/StripeCount even as population grows, instead of processing the entire
    /// population in a single frame once every N frames.
    /// </summary>
    void Update(EngineTime time, byte stripeIndex);

    /// <summary>
    /// How many rotating buckets to split this system's population into. Must be greater
    /// than zero.
    /// </summary>
    byte StripeCount { get; }
}
