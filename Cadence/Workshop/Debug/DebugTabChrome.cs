using Microsoft.Xna.Framework;

namespace Workshop.Debug;

/// <summary>The Debug tab's sizes and colours, in one place.</summary>
public static class DebugTabChrome
{
    public const float Margin = 8f;
    public const float RowHeight = 24f;
    public const float RowGap = 6f;
    public const float SectionTileWidth = 130f;

    /// <summary>The Live section's header lines.</summary>
    public const float HeaderHeight = 64f;

    /// <summary>The frame graph's band in the timeline, and each selected gauge's band under it.</summary>
    public const float FrameBandHeight = 130f;
    public const float GaugeBandHeight = 64f;
    public const float BandGap = 4f;
    public const float BandLabelHeight = 16f;

    /// <summary>The gauge list's columns, from its left edge; the sparkline fills what is left of the row.</summary>
    public const float GaugeListRowHeight = 18f;
    public const float GaugeNameIndent = 16f;
    public const float GaugeValueColumnX = 230f;
    public const float GaugeMinimumColumnX = 320f;
    public const float GaugeMaximumColumnX = 410f;
    public const float GaugeAverageColumnX = 500f;
    public const float GaugeSparklineColumnX = 600f;

    /// <summary>Each gauge's sparkline: one pixel column per point its window is squeezed or stretched into.</summary>
    public const int GaugeSparklineWidth = 512;
    public const float GaugeSparklineVerticalGap = 2f;

    /// <summary>How often the Live section's text is rebuilt; the graphs redraw every frame.</summary>
    public const int TextRefreshFrames = 15;

    /// <summary>One frame at 60 frames per second: the line on the frame graph, and the height above which a frame is a spike.</summary>
    public const double FrameBudgetMilliseconds = 1000.0 / 60.0;

    public static readonly Color GraphBackground = new(25, 25, 25);
    public static readonly Color FrameSeries = Color.LightGreen;
    public static readonly Color SpikeSeries = Color.OrangeRed;
    public static readonly Color FrameBudgetLine = Color.Red * 0.7f;
    public static readonly Color LevelSeries = Color.LightGreen;
    public static readonly Color CumulativeSeries = Color.Orange;
    public static readonly Color GraphLabel = Color.LightGray;
    public static readonly Color Cursor = Color.White * 0.8f;
    public static readonly Color SelectionWash = Color.White * 0.22f;
    public static readonly Color GroupHeading = new(30, 60, 110);
    public static readonly Color SelectedRow = Color.Gold * 0.5f;
    public static readonly Color ColumnHeading = new(70, 70, 70);
    public static readonly Color WarningText = new(150, 70, 0);
    public static readonly Color ToggleOn = new(80, 68, 30);
}
