namespace Presentation.Rendering;

/// <summary>How SparklineRenderer draws a series.</summary>
public enum SparklineStyle
{
    /// <summary>A connected line: for a level, where the shape over time matters.</summary>
    Line,

    /// <summary>Columns rising from the bottom edge: for per-frame events, where each spike matters.</summary>
    Bars,
}
