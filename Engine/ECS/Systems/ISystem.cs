namespace Engine.ECS.Systems;

/// <summary> A system acts on one or more components via Update(). </summary>
/// <cleanupVersion>1</cleanupVersion>
public interface ISystem
{
    ///<summary> Updates the state for the system's components. </summary>
    /// <remarks>
    /// Called every frame by SystemManager, which also owns and advances the rotating
    /// stripeIndex (0..StripeCount-1).  Implementations should process
    /// only the entities assigned to stripeIndex (see EntityStripeSet) rather than gating
    /// the whole population on a period -- this keeps per-frame cost proportional to
    /// Count/StripeCount even as population grows, instead of processing the entire
    /// population in a single frame once every N frames.
    /// </remarks>
    void Update(EngineTime time, byte stripeIndex);

    /// <summary> How many rotating buckets to split this system's population into. Must be greater than zero. </summary>
    byte StripeCount { get; }
}