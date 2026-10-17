namespace Engine.ECS.Systems;

/// <summary> Frame timing passed to systems each update. </summary>
/// <remarks>A minimal Engine-owned equivalent of FNA's GameTime, so Engine never takes a dependency on the rendering framework </remarks>
/// <cleanupVersion>1</cleanupVersion>
public readonly record struct EngineTime(TimeSpan Total, TimeSpan Elapsed, bool IsRunningSlowly, long FrameCount);