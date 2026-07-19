namespace Presentation.UI;

/// <summary>How child windows are arranged within a parent window.</summary>
public enum WindowTileMode
{
    /// <summary>Freely positioned; the creator sets relative position and draw order.</summary>
    Floating,

    /// <summary>Arranged horizontally, tiled with no gap.</summary>
    Horizontal,

    /// <summary>Arranged vertically, tiled with no gap.</summary>
    Vertical,
}
