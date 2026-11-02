using Microsoft.Xna.Framework;

namespace UIEngine.UI;

/// <summary>
/// The title bar, border, and user-interaction capabilities for a window -- "chrome" in the
/// same sense used by IWindowChromeBehavior: everything about a window's shell, independent
/// of what's drawn in its content area (see WindowContentOptions/TextOptions) or how it sizes
/// and positions itself (see WindowLayoutOptions).
/// </summary>
public sealed class ElementChromeOptions
{
    /*========Title========*/
    public bool? ShowTitle { get; init; }

    /// <summary>Whether the title bar still shows when the window is minimized (so title-less windows don't disappear entirely).</summary>
    public bool? ShowTitleWhenMinimized { get; init; }

    public string? TitleText { get; init; }

    public Color? TitleColor { get; init; }
    public Color? TitleTextColor { get; init; }

    /*========Border========*/
    public bool? ShowBorder { get; init; }

    public Vector2? BorderSize { get; init; }

    /// <summary>Defaults to Flat (today's solid-color look) -- Outset/Inset are opt-in for windows.</summary>
    public BorderStyle? BorderStyle { get; init; }

    /// <summary>Only affects BorderStyle.Flat -- Outset/Inset's bevel shading (see BorderRenderer) is independent of this.</summary>
    public Color? BorderColor { get; init; }

    /*========User Controls========*/
    public bool? CanUserClose { get; init; }

    public bool? CanUserMinimize { get; init; }

    /// <summary>Not yet implemented -- reserved for click-and-drag window moving.</summary>
    public bool? CanUserMove { get; init; }

    /// <summary>Not yet implemented -- reserved for drag-to-resize.</summary>
    public bool? CanUserResize { get; init; }

    public bool? CanUserScrollHorizontal { get; init; }

    public bool? CanUserScrollVertical { get; init; }

    /// <summary>Defaults to Auto -- see ScrollbarVisibility.</summary>
    public ScrollbarVisibility? ScrollbarVisibility { get; init; }

    /// <summary>Defaults to true (opt-out, unlike every other CanUserXxx flag here) -- see Window.CanUserFocus.</summary>
    public bool? CanUserFocus { get; init; }
}