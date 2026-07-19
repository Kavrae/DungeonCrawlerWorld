using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI;

public class Button
{
    public Guid ButtonId { get; } = Guid.NewGuid();

    public Window ParentWindow { get; }

    public Vector2 RelativePosition { get; private set; }
    public Vector2 AbsolutePosition { get; private set; }

    public Vector2 Size { get; }
    public static readonly Vector2 DefaultSize = new(50, 50);

    public Rectangle ButtonRectangle { get; private set; }
    public Rectangle ContentRectangle { get; private set; }

    public Color ButtonColor { get; }

    public bool ShowBorder { get; }

    public string Text { get; private set; }
    public Vector2 TextOffset { get; private set; }

    protected SpriteFontBase? Font { get; }

    /// <summary>Raised when the button is clicked.</summary>
    public event Action? Clicked;

    public Button(Window parentWindow, ButtonOptions buttonOptions)
    {
        ArgumentNullException.ThrowIfNull(parentWindow);
        ArgumentNullException.ThrowIfNull(buttonOptions);

        ParentWindow = parentWindow;

        Text = buttonOptions.Text ?? string.Empty;
        TextOffset = buttonOptions.TextOffset ?? new Vector2(2, -4);
        Font = buttonOptions.Font;

        RelativePosition = buttonOptions.RelativePosition ?? Vector2.Zero;
        Size = buttonOptions.Size ?? DefaultSize;
        ShowBorder = buttonOptions.ShowBorder ?? false;
        ButtonColor = buttonOptions.Color ?? Color.White;
    }

    public virtual void Initialize()
    {
        CalculateButtonPositionAndRectangle();
    }

    public virtual void Update(GameTime gameTime)
    {
    }

    // TODO extra border to make 3d + slightly darker color for hover effect
    public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (ShowBorder)
        {
            spriteBatch.Draw(unitRectangle, ButtonRectangle, Color.Black);
        }

        spriteBatch.Draw(unitRectangle, ContentRectangle, ButtonColor);

        if (!string.IsNullOrWhiteSpace(Text) && Font is not null)
        {
            spriteBatch.DrawString(Font, Text, AbsolutePosition + TextOffset, Color.Black);
        }
    }

    public void ChangeRelativePosition(Vector2 newPosition)
    {
        RelativePosition = newPosition;
        CalculateButtonPositionAndRectangle();
    }

    /// <summary>Changes the button's label in place, e.g. a minimize/restore toggle button swapping its glyph to match the window's current state.</summary>
    public void SetText(string text, Vector2 textOffset)
    {
        Text = text ?? string.Empty;
        TextOffset = textOffset;
    }

    public void CalculateButtonPositionAndRectangle()
    {
        AbsolutePosition = RelativePosition + ParentWindow.WindowAbsolutePosition;
        ButtonRectangle = new Rectangle((int)AbsolutePosition.X, (int)AbsolutePosition.Y, (int)Size.X, (int)Size.Y);
        ContentRectangle = ShowBorder
            // Decrease bottom and right by 1 to show those borders.
            ? new Rectangle(ButtonRectangle.X, ButtonRectangle.Y, ButtonRectangle.Width - 1, ButtonRectangle.Height - 1)
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
