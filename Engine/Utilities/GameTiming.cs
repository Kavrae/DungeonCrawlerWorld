namespace Engine.Utilities;

/// <summary> This codebase's standing frame-rate assumption</summary>
/// <remarks>The single source of truth for converting
/// between frames and seconds, so anywhere a duration is expressed in frames (e.g.
/// PotionCooldownEffects.DurationFrames, ActionTargetingController.DoubleTapWindowFrames,
/// HudChrome.HoverTooltipDelayFrames) can state its real-world duration without hardcoding a
/// bare "60", and without those unrelated call sites having to depend on each other to share it.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class GameTiming
{
    public const ushort FramesPerSecond = 60;

    /// <summary> Converts a real-world duration to a frame count at FramesPerSecond</summary>
    /// <remarks>Rounded to the nearest
    /// frame (not truncated) so a duration that doesn't divide evenly (e.g. 0.3s = 18 frames
    /// exactly, but many won't) doesn't systematically undercount by up to a whole frame. Not a
    /// const-compatible expression (a method call, not a constant expression) -- a field that
    /// wants to stay a compile-time const should instead multiply by FramesPerSecond directly
    /// (see PotionCooldownEffects.DurationFrames) and accept truncation, or round by hand.
    /// </remarks>
    public static ushort FramesForSeconds(float seconds) => (ushort)MathF.Round(seconds * FramesPerSecond);
}
