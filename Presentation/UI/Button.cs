using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Rendering;

namespace Presentation.UI;

public class Button
{
    public Guid ButtonId { get; } = Guid.NewGuid();

    public Window ParentWindow { get; }

    public Vector2 RelativePosition { get; private set; }
    public Vector2 AbsolutePosition { get; private set; }

    public Vector2 Size { get; }

    public Rectangle ButtonRectangle { get; private set; }
    public Rectangle ContentRectangle { get; private set; }

    public Rectangle BorderTopRectangle { get; private set; }
    public Rectangle BorderBottomRectangle { get; private set; }
    public Rectangle BorderLeftRectangle { get; private set; }
    public Rectangle BorderRightRectangle { get; private set; }

    public Color ButtonColor { get; }

    public bool ShowBorder { get; }

    /// <summary>Defaults to Outset -- unlike Window (which defaults to Flat), every title button gets the raised bevel look unless a caller opts out.</summary>
    public BorderStyle BorderStyle { get; }

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
    {
        ArgumentNullException.ThrowIfNull(parentWindow);
        ArgumentNullException.ThrowIfNull(buttonOptions);

        ParentWindow = parentWindow;

        Text = buttonOptions.Text ?? string.Empty;

        Font = buttonOptions.Font ?? parentWindow.TitleFont;
        _glyphRenderer = parentWindow.GlyphRenderer;

        RelativePosition = buttonOptions.RelativePosition ?? Vector2.Zero;
        Size = buttonOptions.Size ?? DefaultTitleButtonSize(parentWindow);
        ShowBorder = buttonOptions.ShowBorder ?? true;
        BorderStyle = buttonOptions.BorderStyle ?? BorderStyle.Outset;
        ButtonColor = buttonOptions.Color ?? Color.LightGray;
    }

    private static Vector2 DefaultTitleButtonSize(Window window)
    {
        var side = window.OriginalTitleSize.Y - DefaultSizeTitleInset;
        return new Vector2(side, side);
    }

    public virtual void Initialize()
    {
        CalculateButtonPositionAndRectangle();
    }

    public virtual void Update(GameTime gameTime)
    {
    }

    public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (ShowBorder)
        {
            BorderRenderer.Draw(spriteBatch, unitRectangle, EffectiveBorderStyle, BorderTopRectangle, BorderBottomRectangle, BorderLeftRectangle, BorderRightRectangle);
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
        RelativePosition = newPosition;
        CalculateButtonPositionAndRectangle();
    }

    /// <summary>Changes the button's label in place, e.g. a minimize/restore toggle button swapping its glyph to match the window's current state.</summary>
    public void SetText(string text)
    {
        Text = text ?? string.Empty;
    }

    public void CalculateButtonPositionAndRectangle()
    {
        AbsolutePosition = RelativePosition + ParentWindow.WindowAbsolutePosition;
        ButtonRectangle = new Rectangle((int)AbsolutePosition.X, (int)AbsolutePosition.Y, (int)Size.X, (int)Size.Y);

        if (ShowBorder)
        {
            ContentRectangle = BorderThickness.Inset(ButtonRectangle, DefaultBorderThickness);
            var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(ButtonRectangle, DefaultBorderThickness);
            BorderTopRectangle = top;
            BorderBottomRectangle = bottom;
            BorderLeftRectangle = left;
            BorderRightRectangle = right;
        }
        else
        {
            ContentRectangle = ButtonRectangle;
        }
    }

    public void HandleClick(Point mousePosition)
    {
        OnClickAction(mousePosition);
    }

    protected virtual void OnClickAction(Point mousePosition)
    {
        Clicked?.Invoke();
    }
}