using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Notifications;

namespace Tests.Presentation;

/// <summary>
/// Covers NotificationMinimizeBehavior in isolation (attached directly to a plain Window,
/// the same way WindowChromeButtonTests exercises WindowCloseBehavior/
/// WindowMinimizeRestoreBehavior) -- confirming it adds a standard-looking "_" button to the
/// left of Close, and that clicking it invokes the supplied callback instead of changing the
/// window's own WindowDisplayMode the way the generic minimize/restore behavior would.
/// </summary>
[TestClass]
public sealed class NotificationMinimizeBehaviorTests
{
    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    private static Window CreateWindowWithCloseAndNotificationMinimize(WindowService windowService, Action onMinimize)
    {
        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Test Window", CanUserClose = true },
        });
        window.Initialize();

        // Attached after Initialize(), mirroring NotificationCenter.ShowActive -- Close is
        // already attached (from CanUserClose), so this lands to its left just like the
        // generic minimize/restore behavior would.
        window.AddChromeBehavior(new NotificationMinimizeBehavior(onMinimize));
        return window;
    }

    [TestMethod]
    public void Attach_AddsMinimizeLookingButtonToLeftOfClose()
    {
        var window = CreateWindowWithCloseAndNotificationMinimize(CreateWindowService(), () => { });

        Assert.HasCount(2, window.TitleButtons);
        var closeButton = window.TitleButtons[0];
        var dismissButton = window.TitleButtons[1];
        Assert.IsLessThan(closeButton.RelativePosition.X, dismissButton.RelativePosition.X);
        Assert.AreEqual("_", dismissButton.Text);
    }

    [TestMethod]
    public void ClickingButton_InvokesCallback_WithoutChangingDisplayMode()
    {
        var invoked = false;
        var window = CreateWindowWithCloseAndNotificationMinimize(CreateWindowService(), () => invoked = true);
        var dismissButton = window.TitleButtons[1];

        window.HandleClick(dismissButton.ButtonRectangle.Center);

        Assert.IsTrue(invoked);
        // Unlike WindowMinimizeRestoreBehavior, this button never shrinks the window itself --
        // dismissing/requeuing is entirely the callback's (NotificationCenter's) job.
        Assert.AreEqual(WindowDisplayMode.Fixed, window.WindowDisplay);
    }
}