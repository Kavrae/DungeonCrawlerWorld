namespace Presentation.UI.ChromeBehaviors;

/// <summary>Adds a close ("X") title button that closes the window when clicked.</summary>
public sealed class CloseBehavior : IChromeBehavior
{
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var closeButton = Window.BuildTitleButton(window, "X");
        closeButton.Clicked += _ => window.Close();
        window.AddTitleButton(closeButton);
    }
}