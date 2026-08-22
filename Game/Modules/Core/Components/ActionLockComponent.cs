namespace Game.Modules.Core.Components;

/// <summary>Action-prevention countdown shared across all immediate and delayed actions.</summary>
/// <param name="standardLockFrames">This entity's own lock duration for most actions -- see StandardLockFrames.</param>
/// <param name="currentLockTotalFrames">The total number of frames for which the entity is locked.</param>
/// <param name="currentLockFramesRemaining">The number of frames remaining in the lock.</param>
/// <cleanupVersion>1</cleanupVersion>
public struct ActionLockComponent(ushort standardLockFrames, ushort currentLockTotalFrames, ushort currentLockFramesRemaining)
{
    /// <summary>This entity's own lock duration for most actions</summary>
    /// <remarks>This is an entity's primary lever for "speed"</remarks>
    public ushort StandardLockFrames { get; set; } = standardLockFrames;

    public ushort CurrentLockTotalFrames { get; set; } = currentLockTotalFrames;
    public ushort CurrentLockFramesRemaining { get; set; } = currentLockFramesRemaining;

    public override readonly string ToString() => $"{CurrentLockFramesRemaining}\\{CurrentLockTotalFrames}\nStandardLockFrames : {StandardLockFrames}";
}
