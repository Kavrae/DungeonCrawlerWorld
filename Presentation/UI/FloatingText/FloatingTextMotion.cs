using Engine.Utilities;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;

namespace Presentation.UI.FloatingText;

/// <summary>Where a floating text has moved to and how opaque it is at a given age.</summary>
/// <remarks>
/// Two phases: a rise of RiseDistanceTiles over RiseFrames, easing out while fading in from StartAlpha, then a slower
/// drift at DriftTilesPerSecond while fading out, easing in, to nothing at LifetimeFrames. The horizontal drift runs at a
/// constant speed for the whole lifetime. Offsets are in tiles; negative Y is up the screen.
/// </remarks>
public static class FloatingTextMotion
{
    /// <param name="ageFrames">Frames since the text appeared.</param>
    /// <param name="horizontalDirection">-1 drifts left, 1 drifts right.</param>
    /// <param name="verticalDirection">-1 rises, 1 falls.</param>
    public static Vector2 GetOffsetTiles(int ageFrames, int horizontalDirection, int verticalDirection)
    {
        var riseFrames = FloatingTextChrome.RiseFrames;
        var horizontalTiles = horizontalDirection * FloatingTextChrome.HorizontalDriftTilesPerSecond * ageFrames / GameTiming.FramesPerSecond;

        float verticalTiles;
        if (ageFrames < riseFrames)
        {
            var riseProgress = (float)ageFrames / riseFrames;
            verticalTiles = FloatingTextChrome.RiseDistanceTiles * EaseOut(riseProgress);
        }
        else
        {
            verticalTiles = FloatingTextChrome.RiseDistanceTiles + FloatingTextChrome.DriftTilesPerSecond * (ageFrames - riseFrames) / GameTiming.FramesPerSecond;
        }

        return new Vector2(horizontalTiles, verticalDirection * verticalTiles);
    }

    public static float GetAlpha(int ageFrames)
    {
        var riseFrames = FloatingTextChrome.RiseFrames;
        if (ageFrames < riseFrames)
        {
            return MathHelper.Lerp(FloatingTextChrome.StartAlpha, 1f, (float)ageFrames / riseFrames);
        }

        var fadeFrames = FloatingTextChrome.LifetimeFrames - riseFrames;
        var fadeProgress = MathHelper.Clamp((float)(ageFrames - riseFrames) / fadeFrames, 0f, 1f);
        return 1f - fadeProgress * fadeProgress;
    }

    private static float EaseOut(float progress) => 1f - (1f - progress) * (1f - progress);
}
