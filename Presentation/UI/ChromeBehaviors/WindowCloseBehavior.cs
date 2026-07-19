using Microsoft.Xna.Framework;

namespace Presentation.UI.ChromeBehaviors;

/// <summary>Adds a close ("X") title button that closes the window when clicked.</summary>
public sealed class WindowCloseBehavior : IWindowChromeBehavior
{
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var buttonSize = new Vector2(window.OriginalTitleSize.Y - 4, window.OriginalTitleSize.Y - 4);
        var closeButton = new Button(window, new ButtonOptions
        {
            Color = Color.LightGray,
            Font = window.TitleFont,
            Size = buttonSize,
            Text = "X",
            TextOffset = new Vector2(2, -1),
        });
        closeButton.Clicked += window.Close;
        window.AddTitleButton(closeButton);
    }
}
