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

    public Color ButtonColor { get; }

    public bool ShowBorder { get; }

    public string Text { get; private set; }

    protected SpriteFontBase? Font { get; }

    private readonly GlyphRenderer _glyphRenderer;

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

        // Button is only ever used for title-bar chrome (close/minimize/dismiss) today, so
        // Font/Size/Color/ShowBorder all default to what every one of those needs -- callers
        // only specify what actually differs (Text, and whichever override a future non-title
        // button turns out to need).
        Font = buttonOptions.Font ?? parentWindow.TitleFont;
        _glyphRenderer = parentWindow.GlyphRenderer;

        RelativePosition = buttonOptions.RelativePosition ?? Vector2.Zero;
        Size = buttonOptions.Size ?? DefaultTitleButtonSize(parentWindow);
        ShowBorder = buttonOptions.ShowBorder ?? true;
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

    // TODO slightly darker color for a hover effect.
    // TODO mouse down should switch this to an inset look, mouse up back to outset (Window
    // Chrome) -- needs GameInputController to expose a held-down state, which it doesn't have
    // yet (see the TODO on its mouse handling in Update).
    public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (ShowBorder)
        {
            spriteBatch.Draw(unitRectangle, ButtonRectangle, Color.Black);
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
        ContentRectangle = ShowBorder
            // Inset by 1px on every side so a full border shows all the way around, not just
            // the bottom/right (which used to read as a shadow/bevel rather than a border).
            ? new Rectangle(ButtonRectangle.X + 1, ButtonRectangle.Y + 1, ButtonRectangle.Width - 2, ButtonRectangle.Height - 2)
            : ButtonRectangle;
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
