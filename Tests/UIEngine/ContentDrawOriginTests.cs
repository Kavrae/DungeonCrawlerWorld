using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>Where an element's DrawContent draws from: its content area's top-left, scrolled with it, whether or not it scrolls.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ContentDrawOriginTests
{
    private static TextWindow CreateTextWindow(bool canUserScrollVertical, string text = "Hi")
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var window = pool.CreateElement<TextWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(120, 80), Size = new Vector2(150, 40), MaximumSize = new Vector2(150, 1000), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserScrollVertical = canUserScrollVertical },
            Text = new TextOptions { Text = text },
        });
        window.Initialize();
        return window;
    }

    [TestMethod]
    public void ANonScrollableElement_DrawsFromItsContentAreasTopLeft()
    {
        var window = CreateTextWindow(canUserScrollVertical: false);

        Assert.AreEqual(window.ContentAbsolutePosition, window.ContentDrawOrigin);
        Assert.AreNotEqual(Vector2.Zero, window.ContentDrawOrigin);
    }

    [TestMethod]
    public void AScrolledElement_DrawsFromItsContentAreasTopLeft_LessTheScroll()
    {
        var window = CreateTextWindow(canUserScrollVertical: true, string.Join(' ', Enumerable.Repeat("word", 200)));
        var unscrolledOrigin = window.ContentDrawOrigin;

        window.ScrollBy(new Vector2(0, 25));

        Assert.AreEqual(25f, window.ScrollOffset.Y);
        Assert.AreEqual(unscrolledOrigin - new Vector2(0, 25), window.ContentDrawOrigin);
        Assert.AreEqual(new Vector2(window.ContentRectangle.X, window.ContentRectangle.Y), unscrolledOrigin);
    }

    [TestMethod]
    public void AScreenPosition_ConvertsToLocal_AgainstTheDrawOrigin()
    {
        var window = CreateTextWindow(canUserScrollVertical: false);
        var origin = window.ContentDrawOrigin;

        Assert.AreEqual(new Vector2(5, 7), window.ToContentLocal(origin + new Vector2(5, 7)));
        Assert.AreEqual(new Rectangle(5, 7, 10, 12), window.ToContentLocal(new Rectangle((int)origin.X + 5, (int)origin.Y + 7, 10, 12)));
        Assert.AreEqual(new Rectangle(0, 0, window.ContentRectangle.Width, window.ContentRectangle.Height), window.LocalContentRectangle);
    }
}
