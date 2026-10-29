using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class WindowTitleBarTests
{
    [TestMethod]
    public void TheTitleBar_HoldsTheTitlesWholeLine_WithItsPaddingAboveAndBelow()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var window = pool.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "(895, 195) 299x596" },
        });
        window.Initialize();

        Assert.AreEqual(window.TitleFont.LineHeight + window.TitlePadding.Y * 2, window.HeaderSize.Y);
    }
}
