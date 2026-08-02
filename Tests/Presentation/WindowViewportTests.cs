using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Regression test for a bug found while visually verifying Window Chrome Phase C
/// (drag-to-move): Window.Initialize used to set WindowViewport exactly once, from
/// _contentState.Rectangle at that moment, and never again -- so DrawContent (which renders
/// through WindowViewport, see Window.Draw) kept painting content at the window's original
/// screen position even after SetRelativePosition/SetSize/SetBounds moved every other
/// rectangle (title, border, content background) to the new one. Border/title track position
/// live because RecalculateRectangles recomputes them on every Arrange; WindowViewport now
/// does too.
/// </summary>
[TestClass]
public sealed class WindowViewportTests
{
    private static ElementPoolService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    [TestMethod]
    public void SetRelativePosition_UpdatesWindowViewportToMatchContentRectangle()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "Test" },
        });
        window.Initialize();

        window.SetRelativePosition(new Vector2(300, 250));

        Assert.AreEqual(window.ContentRectangle, window.Viewport.Bounds);
    }

    [TestMethod]
    public void SetSize_UpdatesWindowViewportToMatchContentRectangle()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            // Explicit MaximumSize larger than the resize target -- otherwise it defaults to
            // this same Size (see BuildWindow), silently clamping SetSize(400, 300) back down
            // to a no-op and making this test pass even without the fix it's guarding.
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(200, 100), MaximumSize = new Vector2(500, 400), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "Test" },
        });
        window.Initialize();

        window.SetSize(new Vector2(400, 300));

        Assert.AreEqual(window.ContentRectangle, window.Viewport.Bounds);
    }

    /// <summary>
    /// Regression test for a second bug found in the same visual pass: a root WrapContent
    /// TextWindow (e.g. a notification popup) computed its content width as MaximumSize.X
    /// minus RelativePosition.X -- correct for a *child* window's MaximumSize (inherited as
    /// the parent's ContentSize, so it's a parent-relative boundary the child's own offset
    /// eats into), but wrong for a root window's explicit, literal MaximumSize, which has no
    /// parent edge to offset against. Dragging a root notification right shrank its computed
    /// width by exactly the same amount, pinning WindowRectangle.Right to a fixed screen x
    /// (MaximumSize.X) regardless of drag position -- visible as "every edge follows the drag
    /// except the right one".
    /// </summary>
    [TestMethod]
    public void DraggingARootWrapContentTextWindow_KeepsItsWidthConstant()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateElement<TextWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(200, 200), MaximumSize = new Vector2(400, 300), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "Test" },
            Text = new TextOptions { Text = "Hello world" },
        });
        window.Initialize();
        var widthBeforeDrag = window.CurrentSize.X;

        window.SetRelativePosition(new Vector2(350, 200));

        Assert.AreEqual(widthBeforeDrag, window.CurrentSize.X);
    }
}