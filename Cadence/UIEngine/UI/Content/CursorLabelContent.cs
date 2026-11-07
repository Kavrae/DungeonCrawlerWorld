using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Geometry;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI.Content;

/// <summary>A one-line label drawn just above the mouse cursor while its owner keeps it shown -- the name of what's hovered.</summary>
/// <remarks>
/// Hosted the way CursorTextContent is: a zero-size User-tier window, drawing at the live cursor. Owner-checked like
/// TooltipController, so one poller's routine Hide never clears what another is showing. Showing the same text every
/// update measures nothing.
/// </remarks>
public sealed class CursorLabelContent(PointerState pointerState, FontService fontService) : IElementContent
{
    /// <summary>Gap between the label's bottom edge and the cursor's hotspot.</summary>
    private const float GapAboveCursor = 4f;

    /// <summary>Background margin around the text on every side.</summary>
    private static readonly Vector2 TextPadding = new(4, 2);

    private static readonly Color TextColor = Color.White;

    private SpriteFontBase? _font;
    private Window _hostWindow = null!;
    private object? _currentOwner;
    private string _text = string.Empty;
    private Vector2 _textSize;

    private SpriteFontBase Font => _font ??= fontService.GetFont(FontChrome.DefaultFontSize);

    /// <summary>The label showing now, or empty -- for tests.</summary>
    public string ShownText => _currentOwner is null ? string.Empty : _text;

    /// <summary>Shows text above the cursor on owner's behalf, taking the label over from whoever showed it before.</summary>
    public void Show(object owner, string text)
    {
        _currentOwner = owner;
        if (text == _text)
        {
            return;
        }

        _text = text;
        _textSize = Font.MeasureString(text);
    }

    /// <summary>Hides the label if owner is the one showing it; otherwise does nothing.</summary>
    public void Hide(object owner)
    {
        if (ReferenceEquals(owner, _currentOwner))
        {
            _currentOwner = null;
        }
    }

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    public void Update(GameTime gameTime)
    {
    }

    public void DrawContent(GameTime gameTime)
    {
        if (_currentOwner is null || _text.Length == 0)
        {
            return;
        }

        var backgroundSize = _textSize + TextPadding * 2;
        var cursor = pointerState.CursorPosition;
        var screenPosition = new Vector2(cursor.X - backgroundSize.X / 2f, cursor.Y - GapAboveCursor - backgroundSize.Y);
        screenPosition = RectangleClamp.ClampInside(screenPosition, backgroundSize, _hostWindow.Render.ScreenSize);

        var position = _hostWindow.ToContentLocal(screenPosition);
        var background = new Rectangle((int)position.X, (int)position.Y, (int)backgroundSize.X, (int)backgroundSize.Y);
        _hostWindow.Render.Batch.Draw(_hostWindow.Render.UnitRectangle, background, WindowPalette.PanelBackgroundColor);
        LabelRenderer.Draw(_hostWindow.Render.Batch, Font, _text, position + TextPadding, TextColor);
    }
}
