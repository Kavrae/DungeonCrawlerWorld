namespace Engine.Utilities;

/// <summary>
/// This codebase's standing frame-rate assumption -- the single source of truth for converting
/// between frames and seconds, so anywhere a duration is expressed in frames (e.g.
/// PotionCooldownEffects.DurationFrames, ActionTargetingController.DoubleTapWindowFrames,
/// HudMetrics.HoverTooltipDelayFrames) can state its real-world duration without hardcoding a
/// bare "60", and without those unrelated call sites having to depend on each other to share it.
/// </summary>
public static class GameTiming
{
    public const int FramesPerSecond = 60;

    /// <summary>
    /// Converts a real-world duration to a frame count at FramesPerSecond, rounded to the nearest
    /// frame (not truncated) so a duration that doesn't divide evenly (e.g. 0.3s = 18 frames
    /// exactly, but many won't) doesn't systematically undercount by up to a whole frame. Not a
    /// const-compatible expression (a method call, not a constant expression) -- a field that
    /// wants to stay a compile-time const should instead multiply by FramesPerSecond directly
    /// (see PotionCooldownEffects.DurationFrames) and accept truncation, or round by hand.
    /// </summary>
    public static int FramesForSeconds(float seconds) => (int)System.MathF.Round(seconds * FramesPerSecond);
}
