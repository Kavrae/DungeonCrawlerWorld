namespace Engine.ECS.Systems;

/// <summary>
/// Frame timing passed to systems each update. A minimal Engine-owned equivalent of
/// FNA's GameTime, so Engine never takes a dependency on the rendering framework -- the
/// exe's game loop converts FNA's GameTime into this once per frame. FrameCount is a shared
/// per-real-frame counter (not per-stripe-cycle -- every system sees the same value each
/// frame) so any system that wants to spread throttled work across entities (e.g.
/// ProcessingTierSystem's consumers) has a common value to combine with entityId, instead of
/// each system inventing and maintaining its own counter.
/// </summary>
public readonly record struct EngineTime(TimeSpan Total, TimeSpan Elapsed, bool IsRunningSlowly, long FrameCount);