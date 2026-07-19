namespace Engine.ECS.Systems;

/// <summary>
/// Frame timing passed to systems each update. A minimal Engine-owned equivalent of
/// FNA's GameTime, so Engine never takes a dependency on the rendering framework -- the
/// exe's game loop converts FNA's GameTime into this once per frame.
/// </summary>
public readonly record struct EngineTime(TimeSpan Total, TimeSpan Elapsed, bool IsRunningSlowly);
