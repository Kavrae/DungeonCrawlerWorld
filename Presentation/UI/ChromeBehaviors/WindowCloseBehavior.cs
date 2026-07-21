namespace Presentation.UI.ChromeBehaviors;

/// <summary>Adds a close ("X") title button that closes the window when clicked.</summary>
public sealed class WindowCloseBehavior : IWindowChromeBehavior
{
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var closeButton = new Button(window, new ButtonOptions { Text = "X" });
        closeButton.Clicked += window.Close;
        window.AddTitleButton(closeButton);
    }
}
