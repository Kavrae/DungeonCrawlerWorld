using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// A short-lived text message that fades in near the mouse cursor and disappears -- the "toast"
/// equivalent for cursor-adjacent feedback. General-purpose (Show(string) takes any text), not a
/// copy-only special case -- the first caller is TextBox showing "Copied" after a successful
/// Ctrl+C/Ctrl+X, but any future feature wanting the same brief, non-blocking confirmation can
/// call Show directly. Hosted the same way DragGhostContent is (zero-size, fully transparent,
/// User-tier window -- see ShellBootstrapper.BuildUserWindows): both draw directly at the
/// live mouse position rather than relative to any window's own bounds, and User is the topmost
/// tier, so this always renders above whatever it's reporting on.
/// </summary>
public sealed class CursorTextContent(FontService fontService, GlyphRenderer glyphRenderer) : IElementContent
{
    /// <summary>
    /// How DrawContent finds the live cursor position -- assigned once ShellBootstrapper.Build
    /// has constructed a real UiInputController, which happens after this class does (see that
    /// method's own comment on why). Defaults to a no-op origin so an unwired instance (e.g. in a
    /// test) never null-refs.
    /// </summary>
    public Func<Point> GetCursorPosition { get; set; } = static () => Point.Zero;
    /// <summary>Total time a message stays visible, including the fade -- roughly a standard toast duration.</summary>
    private static readonly int DisplayFrames = GameTiming.FramesForSeconds(1.0f);

    /// <summary>How much of DisplayFrames, at the end, is spent fading to transparent rather than fully opaque.</summary>
    private static readonly int FadeFrames = GameTiming.FramesForSeconds(0.3f);

    /// <summary>Offset from the live cursor position -- drawn near, not directly under, the pointer hotspot.</summary>
    private static readonly Vector2 CursorOffset = new(12, 12);

    private static readonly Color TextColor = Color.White;

    private readonly SpriteFontBase _font = fontService.GetFont(14);

    private Window _hostWindow = null!;

    private string _text = string.Empty;
    private int _remainingFrames;

    /// <summary>Starts (or restarts) the display/fade countdown with the given text -- e.g. TextBox calling Show("Copied") after a successful Ctrl+C/Ctrl+X.</summary>
    public void Show(string text)
    {
        _text = text;
        _remainingFrames = DisplayFrames;
    }

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    public void Update(GameTime gameTime)
    {
        if (_remainingFrames > 0)
        {
            _remainingFrames--;
        }
    }

    public void DrawContent(GameTime gameTime)
    {
        if (_remainingFrames <= 0)
        {
            return;
        }

        var alpha = _remainingFrames < FadeFrames ? (float)_remainingFrames / FadeFrames : 1f;
        var mousePosition = GetCursorPosition();
        var position = new Vector2(mousePosition.X, mousePosition.Y) + CursorOffset;

        glyphRenderer.Draw(_hostWindow.ElementPoolService.SpriteBatch, _font, _text, position, TextColor * alpha);
    }
}
