using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>Covers Window.SetGlow's bookkeeping -- the actual ring rendering (GlowRenderer) is visual and verified in-game, not unit-testable without a GraphicsDevice.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowGlowTests
{
    private static ElementPoolService CreateWindowService() => TestElementPoolServiceFactory.Create(TestFonts.Shared);

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

    private static readonly GlowAnimation Pulse = GlowAnimation.Pulse(TimeSpan.FromSeconds(2));
    private static readonly TimeSpan HalfAPeriod = TimeSpan.FromSeconds(1);

    [TestMethod]
    public void NewWindow_GlowIsStatic()
    {
        var window = CreateWindow(CreateWindowService());

        Assert.AreEqual(GlowAnimation.Static, window.GlowAnimation);
    }

    [TestMethod]
    public void SetGlow_WithAPulse_StoresIt()
    {
        var window = CreateWindow(CreateWindowService());

        window.SetGlow(true, animation: Pulse);

        Assert.AreEqual(Pulse, window.GlowAnimation);
    }

    [TestMethod]
    public void SetGlow_TurningOn_StartsThePulseAtTheNextDraw()
    {
        var window = CreateWindow(CreateWindowService());
        window.StartPendingGlowPulseAt(TimeSpan.FromSeconds(10));

        window.SetGlow(true, animation: Pulse);
        window.StartPendingGlowPulseAt(TimeSpan.FromSeconds(20));

        Assert.AreEqual(1f, window.GlowStrengthAt(TimeSpan.FromSeconds(20)), 1e-5f);
        Assert.AreEqual(0.25f, window.GlowStrengthAt(TimeSpan.FromSeconds(20) + HalfAPeriod), 1e-5f);
    }

    [TestMethod]
    public void SetGlow_AgainWhileGlowingWithTheSameAnimation_LeavesThePulseRunning()
    {
        var window = CreateWindow(CreateWindowService());
        window.SetGlow(true, animation: Pulse);
        window.StartPendingGlowPulseAt(TimeSpan.Zero);

        window.SetGlow(true, animation: Pulse);

        Assert.AreEqual(0.25f, window.GlowStrengthAt(HalfAPeriod), 1e-5f);
    }

    [TestMethod]
    public void SetGlow_WithADifferentAnimation_RestartsThePulse()
    {
        var window = CreateWindow(CreateWindowService());
        window.SetGlow(true, animation: Pulse);
        window.StartPendingGlowPulseAt(TimeSpan.Zero);

        window.SetGlow(true, animation: GlowAnimation.Pulse(TimeSpan.FromSeconds(4)));

        Assert.AreEqual(1f, window.GlowStrengthAt(HalfAPeriod), 1e-5f);
    }

    [TestMethod]
    public void RestartGlowPulse_StartsThePulseOverAtTheNextDraw()
    {
        var window = CreateWindow(CreateWindowService());
        window.SetGlow(true, animation: Pulse);
        window.StartPendingGlowPulseAt(TimeSpan.Zero);

        window.RestartGlowPulse();
        window.StartPendingGlowPulseAt(HalfAPeriod);

        Assert.AreEqual(1f, window.GlowStrengthAt(HalfAPeriod), 1e-5f);
        Assert.AreEqual(0.25f, window.GlowStrengthAt(HalfAPeriod * 2), 1e-5f);
    }

    [TestMethod]
    public void GlowStrengthAt_NeverStartsThePulse()
    {
        var window = CreateWindow(CreateWindowService());
        window.SetGlow(true, animation: Pulse);
        window.GlowStrengthAt(TimeSpan.Zero);

        window.StartPendingGlowPulseAt(HalfAPeriod);

        Assert.AreEqual(1f, window.GlowStrengthAt(HalfAPeriod), 1e-5f);
        Assert.AreEqual(0.25f, window.GlowStrengthAt(HalfAPeriod * 2), 1e-5f);
    }

    [TestMethod]
    public void SetGlow_AfterItsCycles_HoldsFullStrength()
    {
        var window = CreateWindow(CreateWindowService());
        window.SetGlow(true, animation: Pulse);
        window.StartPendingGlowPulseAt(TimeSpan.Zero);

        Assert.AreEqual(1f, window.GlowStrengthAt(TimeSpan.FromSeconds(10.5)));
    }

    [TestMethod]
    public void SetOverlayGlow_PulsesAndRestartsTheSameWay()
    {
        var window = CreateWindow(CreateWindowService());
        window.SetOverlayGlow(true, animation: Pulse);
        window.StartPendingOverlayGlowPulseAt(TimeSpan.Zero);

        Assert.AreEqual(0.25f, window.OverlayGlowStrengthAt(HalfAPeriod), 1e-5f);

        window.RestartOverlayGlowPulse();

        Assert.AreEqual(1f, window.OverlayGlowStrengthAt(HalfAPeriod), 1e-5f);
    }

    [TestMethod]
    public void CreateWindow_ReusingAPooledPulsingWindow_ResetsItToStatic()
    {
        var windowService = CreateWindowService();
        var window = CreateWindow(windowService);
        window.SetGlow(true, animation: Pulse);
        window.SetOverlayGlow(true, animation: Pulse);
        window.Close();

        var reused = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(100, 50), DisplayMode = ElementDisplayMode.Fixed },
        });

        Assert.AreSame(window, reused);
        Assert.AreEqual(GlowAnimation.Static, reused.GlowAnimation);
        Assert.AreEqual(1f, reused.GlowStrengthAt(HalfAPeriod));
        Assert.AreEqual(1f, reused.OverlayGlowStrengthAt(HalfAPeriod));
    }
}
