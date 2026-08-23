using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>Covers Window.SetGlow's bookkeeping -- the actual ring rendering (GlowRenderer) is visual and verified in-game, not unit-testable without a GraphicsDevice.</summary>
[TestClass]
public sealed class WindowGlowTests
{
    private static ElementPoolService CreateWindowService() => TestElementPoolServiceFactory.Create(new FontService("Fonts"), new LabelRenderer());

    private static Window CreateWindow(ElementPoolService windowService)
    {
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(100, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        window.Initialize();
        return window;
    }

    [TestMethod]
    public void NewWindow_IsNotGlowingByDefault()
    {
        var window = CreateWindow(CreateWindowService());

        Assert.IsFalse(window.IsGlowing);
    }

    [TestMethod]
    public void SetGlow_True_SetsIsGlowing()
    {
        var window = CreateWindow(CreateWindowService());

        window.SetGlow(true);

        Assert.IsTrue(window.IsGlowing);
        Assert.AreEqual(Color.Gold, window.GlowColor);
    }

    [TestMethod]
    public void SetGlow_WithCustomColor_UsesThatColor()
    {
        var window = CreateWindow(CreateWindowService());

        window.SetGlow(true, Color.Red);

        Assert.AreEqual(Color.Red, window.GlowColor);
    }

    [TestMethod]
    public void SetGlow_False_ClearsIsGlowing()
    {
        var window = CreateWindow(CreateWindowService());
        window.SetGlow(true);

        window.SetGlow(false);

        Assert.IsFalse(window.IsGlowing);
    }

    /// <summary>Regression guard for pooled-window reuse -- a window handed back out of WindowService's pool must not still be glowing from whatever it was last used for.</summary>
    [TestMethod]
    public void CreateWindow_ReusingAPooledGlowingWindow_ResetsGlow()
    {
        var windowService = CreateWindowService();
        var window = CreateWindow(windowService);
        window.SetGlow(true);
        window.Close();

        var reused = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(100, 50), DisplayMode = ElementDisplayMode.Fixed },
        });

        Assert.AreSame(window, reused);
        Assert.IsFalse(reused.IsGlowing);
    }
}
