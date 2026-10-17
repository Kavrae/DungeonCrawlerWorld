using Microsoft.Xna.Framework;

namespace Presentation.UI.Chrome;

/// <summary>The Diagnostics window's position, size and internal layout -- see HudChrome's own doc comment for why these are plain mutable fields rather than readonly.</summary>
public static class DiagnosticsWindowChrome
{
    public static Vector2 WindowPosition = new(30, 80);
    public static Vector2 WindowSize = new(480, 620);

    /// <summary>Space between the content area's edge and the rows.</summary>
    public static float RowInset = 4f;

    /// <summary>One gauge row's height; its sparkline fills the row less SparklineVerticalGap top and bottom.</summary>
    public static float RowHeight = 16f;
    public static float SparklineVerticalGap = 2f;
    public static float SparklineWidth = 150f;

    /// <summary>Where the value and min-max columns start, measured from the row's left edge.</summary>
    public static float ValueColumnX = 150f;
    public static float RangeColumnX = 220f;

    /// <summary>The frame-time graph's height, under its label row.</summary>
    public static float FrameGraphHeight = 48f;

    /// <summary>Extra space above a group's heading, separating it from the rows before.</summary>
    public static float GroupGap = 6f;

    /// <summary>Frame time the frame graph marks with a line: one frame at 60 frames per second.</summary>
    public static double FrameBudgetMilliseconds = 1000.0 / 60.0;

    /// <summary>How often the window's text is re-formatted; the graphs redraw every frame regardless.</summary>
    public static double TextRefreshIntervalSeconds = 0.25;
}
