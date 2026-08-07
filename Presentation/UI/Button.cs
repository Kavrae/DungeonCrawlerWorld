using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Rendering;

namespace Presentation.UI;

/// <summary>
/// A clickable, bordered box with centered text -- an Element in its own right (not a Window
/// subclass), so it doesn't carry any of Window's title/content/hierarchy/chrome-behavior
/// baggage. Reuses Element's own geometry/border/content state directly (RelativePosition/
/// Size/ContentRectangle/ShowBorder/BorderStyle/BorderTopRectangle etc. are all the inherited
/// Element properties, just populated by CalculateButtonPositionAndRectangle instead of
/// Element's Measure/Arrange pipeline, which a simple one-rectangle control like this has no
/// use for) rather than hand-rolling a second, parallel copy of the same rectangle/border math.
/// </summary>
public class Button : Element
{
    public Guid ButtonId { get; } = Guid.NewGuid();

    /// <summary>The window whose title bar hosts this button -- distinct from Element's own ParentElement (the ChildElements hierarchy), which a title button never participates in.</summary>
    // TODO if buttons are ever attached to non-Window elements (e.g. a header button on
    // Folder), HostWindow needs to widen to Element and DefaultTitleButtonSize below needs a
    // non-Window-specific default size, since it currently derives from OriginalTitleSize (a
    // Window-only text-title-bar concept Folder's icon header has no equivalent of).
    public Window HostWindow { get; }

    public Color ButtonColor { get; }

    public string Text { get; private set; }

    /// <summary>True while the mouse is held down over this button -- Draw() swaps Outset/Inset while true, giving the pressed-in look. See GameInputController, which calls SetPressed on press/release.</summary>
    public bool IsPressed { get; private set; }

    protected SpriteFontBase? Font { get; }

    private readonly GlyphRenderer _glyphRenderer;
    private static readonly BorderThickness DefaultBorderThickness = BorderThickness.Uniform(Vector2.One);

    /// <summary>Raised when the button is clicked.</summary>
    public event Action? Clicked;

    /// <summary>Inset from the title bar's own height each title button's default square size shrinks by, leaving a small margin above/below it.</summary>
    private const float DefaultSizeTitleInset = 4;

    public Button(Window parentWindow, ButtonOptions buttonOptions)
        : base((parentWindow ?? throw new ArgumentNullException(nameof(parentWindow))).FontService, parentWindow.ElementPoolService, parentWindow.GlyphRenderer)
    {
        ArgumentNullException.ThrowIfNull(buttonOptions);

        HostWindow = parentWindow;

        Text = buttonOptions.Text ?? string.Empty;

        Font = buttonOptions.Font ?? parentWindow.TitleFont;
        _glyphRenderer = parentWindow.GlyphRenderer;

        _geometry.RelativePosition = buttonOptions.RelativePosition ?? Vector2.Zero;
        _geometry.CurrentSize = buttonOptions.Size ?? DefaultTitleButtonSize(parentWindow);

        _border.Show = buttonOptions.ShowBorder ?? true;
        // Defaults to Outset -- unlike Window (which defaults to Flat), every title button gets the raised bevel look unless a caller opts out.
        _border.Style = buttonOptions.BorderStyle ?? BorderStyle.Outset;
        _border.Thickness = DefaultBorderThickness;

        ButtonColor = buttonOptions.Color ?? Color.LightGray;
    }

    private static Vector2 DefaultTitleButtonSize(Window window)
    {
        var side = window.OriginalTitleSize.Y - DefaultSizeTitleInset;
        return new Vector2(side, side);
    }

    public override void Initialize()
    {
        CalculateButtonPositionAndRectangle();
    }

    public override void Update(GameTime gameTime)
    {
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (ShowBorder)
        {
            BorderRenderer.Draw(spriteBatch, unitRectangle, EffectiveBorderStyle, BorderColor, BorderTopRectangle, BorderBottomRectangle, BorderLeftRectangle, BorderRightRectangle);
        }

        spriteBatch.Draw(unitRectangle, ContentRectangle, ButtonColor);

        if (!string.IsNullOrWhiteSpace(Text) && Font is not null)
        {
            // Same ink-centering GlyphRenderer uses for map tile glyphs -- centers on the
            // string's actual rendered ink within ContentRectangle, rather than a manually
            // tuned per-glyph pixel offset that has to be re-eyeballed for every new label.
            _glyphRenderer.DrawCentered(
                spriteBatch,
                Font,
                Text,
                new Vector2(ContentRectangle.X, ContentRectangle.Y),
                new Vector2(ContentRectangle.Width, ContentRectangle.Height),
                Color.Black);
        }
    }

    /// <summary>Outset<->Inset while IsPressed (the raised bevel briefly reads as pushed in); Flat is unaffected, since it has no bevel direction to swap.</summary>
    private BorderStyle EffectiveBorderStyle => IsPressed
        ? BorderStyle switch
        {
            BorderStyle.Outset => BorderStyle.Inset,
            BorderStyle.Inset => BorderStyle.Outset,
            _ => BorderStyle,
        }
        : BorderStyle;

    public void SetPressed(bool isPressed)
    {
        IsPressed = isPressed;
    }

    public void ChangeRelativePosition(Vector2 newPosition)
    {
        _geometry.RelativePosition = newPosition;
        CalculateButtonPositionAndRectangle();
    }

    /// <summary>Changes the button's label in place, e.g. a minimize/restore toggle button swapping its glyph to match the window's current state.</summary>
    public void SetText(string text)
    {
        Text = text ?? string.Empty;
    }

    public void CalculateButtonPositionAndRectangle()
    {
        _geometry.AbsolutePosition = _geometry.RelativePosition + HostWindow.AbsolutePosition;
        _geometry.Rectangle = new Rectangle((int)_geometry.AbsolutePosition.X, (int)_geometry.AbsolutePosition.Y, (int)_geometry.CurrentSize.X, (int)_geometry.CurrentSize.Y);

        if (ShowBorder)
        {
            _contentState.Rectangle = BorderThickness.Inset(_geometry.Rectangle, DefaultBorderThickness);
            var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(_geometry.Rectangle, DefaultBorderThickness);
            _border.TopRectangle = top;
            _border.BottomRectangle = bottom;
            _border.LeftRectangle = left;
            _border.RightRectangle = right;
        }
        else
        {
            _contentState.Rectangle = _geometry.Rectangle;
        }
    }

    public new void HandleClick(Point mousePosition)
    {
        OnClickAction(mousePosition);
    }

    protected virtual void OnClickAction(Point mousePosition)
    {
        Clicked?.Invoke();
    }
}
