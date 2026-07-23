using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// The title bar, border, and user-interaction capabilities for a window -- "chrome" in the
/// same sense used by IWindowChromeBehavior: everything about a window's shell, independent
/// of what's drawn in its content area (see WindowContentOptions/TextOptions) or how it sizes
/// and positions itself (see WindowLayoutOptions).
/// </summary>
public sealed class WindowChromeOptions
{
    /*========Title========*/
    public bool? ShowTitle { get; set; }

    /// <summary>Whether the title bar still shows when the window is minimized (so title-less windows don't disappear entirely).</summary>
    public bool? ShowTitleWhenMinimized { get; set; }

    public string? TitleText { get; set; }

    public Color? TitleColor { get; set; }
    public Color? FocusedTitleColor { get; set; }

    /*========Border========*/
    public bool? ShowBorder { get; set; }

    public Vector2? BorderSize { get; set; }

    /// <summary>Defaults to Flat (today's solid-color look) -- Outset/Inset are opt-in for windows.</summary>
    public BorderStyle? BorderStyle { get; set; }

    /*========User Controls========*/
    public bool? CanUserClose { get; set; }

    public bool? CanUserMinimize { get; set; }

    /// <summary>Not yet implemented -- reserved for click-and-drag window moving.</summary>
    public bool? CanUserMove { get; set; }

    /// <summary>Not yet implemented -- reserved for drag-to-resize.</summary>
    public bool? CanUserResize { get; set; }

    /// <summary>Not yet implemented.</summary>
    public bool? CanUserScrollHorizontal { get; set; }

    /// <summary>Not yet implemented.</summary>
    public bool? CanUserScrollVertical { get; set; }

    /// <summary>Defaults to true (opt-out, unlike every other CanUserXxx flag here) -- see Window.CanUserFocus.</summary>
    public bool? CanUserFocus { get; set; }
}