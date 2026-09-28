using Engine.Utilities;

namespace Presentation.UI.Chrome;

/// <summary>Timing, motion and capacity constants for the floating text drawn over the map.</summary>
/// <remarks>Plain mutable fields for the same reason as the other *Chrome classes: a future data-driven theme loader can overwrite them at startup.</remarks>
public static class FloatingTextChrome
{
    /// <summary>How long a floating text lives, from appearing to fading out completely.</summary>
    public static int LifetimeFrames = 2 * GameTiming.FramesPerSecond;

    /// <summary>The opening rise, during which the text fades in from StartAlpha to fully opaque.</summary>
    public static int RiseFrames = (int)(0.4f * GameTiming.FramesPerSecond);

    public static float RiseDistanceTiles = 1f;
    public static float StartAlpha = 0.5f;

    /// <summary>Vertical speed after the rise, while the text fades out.</summary>
    public static float DriftTilesPerSecond = 0.4f;

    /// <summary>Horizontal speed for the whole lifetime; the direction is chosen at random per text.</summary>
    public static float HorizontalDriftTilesPerSecond = 0.3f;

    /// <summary>A critical hit's text starts CritPopScale times its normal size and shrinks back to it over CritPopFrames.</summary>
    public static int CritPopFrames = (int)(0.1f * GameTiming.FramesPerSecond);
    public static float CritPopScale = 1.5f;

    /// <summary>How far from the entity's centre a text can appear: numbers to the left, statuses to the right.</summary>
    public static float SpawnJitterTiles = 0.5f;

    /// <summary>Time between one entity's consecutive texts, so simultaneous events fall in a readable waterfall instead of stacking.</summary>
    public static int ReleaseIntervalFrames = (int)(0.1f * GameTiming.FramesPerSecond);

    /// <summary>Texts waiting on one entity before a new one of the same kind is added into the newest waiting one instead of queued.</summary>
    public static int MaxBacklogPerEntity = 8;

    /// <summary>Texts on screen at once; the oldest is retired early to make room.</summary>
    public static int MaxActiveTexts = 256;
}
