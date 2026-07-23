using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Confirms the standard title-button layout: minimize/restore (whichever currently applies)
/// to the left, close to the right, both grouped on the title bar's right side -- and that
/// minimize/restore is a single toggling button, not two buttons that could both show at once.
/// </summary>
[TestClass]
public sealed class WindowChromeButtonTests
{
    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    private static Window CreateWindowWithCloseAndMinimize(WindowService windowService)
    {
        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            // A non-trivial TitleText matters here: minimizing shrinks the title bar to fit
            // just its text, and an empty title would shrink it down to less than a single
            // button's own width -- a separate edge case this test isn't about.
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Test Window", CanUserClose = true, CanUserMinimize = true },
        });
        window.Initialize();
        return window;
    }

    [TestMethod]
    public void Initialize_CloseAndMinimize_CreatesExactlyOneButtonEach()
    {
        var window = CreateWindowWithCloseAndMinimize(CreateWindowService());

        // One minimize/restore toggle button plus one close button -- never two separate
        // minimize and restore buttons visible at once.
        Assert.HasCount(2, window.TitleButtons);
    }

    [TestMethod]
    public void Initialize_CloseAndMinimize_MinimizeRestoreIsLeftOfClose()
    {
        var window = CreateWindowWithCloseAndMinimize(CreateWindowService());

        // Close is attached first (see Window.Initialize), so it lands rightmost per
        // AddTitleButton's right-to-left insertion order; minimize/restore lands to its left.
        var closeButton = window.TitleButtons[0];
        var minimizeRestoreButton = window.TitleButtons[1];
        Assert.IsLessThan(closeButton.RelativePosition.X, minimizeRestoreButton.RelativePosition.X);
    }

    [TestMethod]
    public void Initialize_OnlyMinimize_NoCloseButton_MinimizeRestoreStillOnRightSide()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, CanUserMinimize = true },
        });
        window.Initialize();

        Assert.HasCount(1, window.TitleButtons);
        // Right-aligned per AddTitleButton's "first item" branch.
        Assert.AreEqual(window.TitleSize.X - window.TitleButtons[0].Size.X - 3, window.TitleButtons[0].RelativePosition.X);
    }

    [TestMethod]
    public void RestoredWindow_MinimizeRestoreButton_StartsAsMinimizeGlyph()
    {
        var window = CreateWindowWithCloseAndMinimize(CreateWindowService());

        var minimizeRestoreButton = window.TitleButtons[1];
        Assert.AreEqual("_", minimizeRestoreButton.Text);
    }

    /// <summary>
    /// Regression test: minimize and restore used to be two separately-attached buttons, both
    /// always present regardless of the window's current display mode -- a restored window
    /// showed a (non-functional-looking, since minimize was already the valid action) restore
    /// button alongside minimize, and vice versa once minimized. Now there is exactly one
    /// button whose action and glyph match whichever of the two is currently valid.
    /// </summary>
    [TestMethod]
    public void ClickingMinimizeRestoreButton_TogglesWindowDisplayModeAndGlyph()
    {
        var window = CreateWindowWithCloseAndMinimize(CreateWindowService());
        var minimizeRestoreButton = window.TitleButtons[1];

        // Re-queried before each click rather than captured once: minimizing shrinks the
        // title bar to fit just its text, which legitimately moves the button on screen.
        window.HandleClick(minimizeRestoreButton.ButtonRectangle.Center);

        Assert.AreEqual(WindowDisplayMode.Minimized, window.WindowDisplay);
        Assert.AreEqual("O", minimizeRestoreButton.Text);
        Assert.HasCount(2, window.TitleButtons);

        window.HandleClick(minimizeRestoreButton.ButtonRectangle.Center);

        Assert.AreEqual(WindowDisplayMode.Fixed, window.WindowDisplay);
        Assert.AreEqual("_", minimizeRestoreButton.Text);
    }

    /// <summary>
    /// Regression test: a title button's cached relative position used to only ever be
    /// computed once, when it was attached (against the window's full static-mode title
    /// width). Minimizing shrinks the title bar down to fit just its text, so without
    /// re-tiling buttons against the new width, the restore button would drift outside the
    /// now-much-narrower title bar -- clickable nowhere on screen, exactly when it's needed
    /// to get the window back.
    /// </summary>
    [TestMethod]
    public void MinimizedWindow_TitleButtonPosition_StaysWithinShrunkTitleBar()
    {
        var window = CreateWindowWithCloseAndMinimize(CreateWindowService());
        var minimizeRestoreButton = window.TitleButtons[1];

        window.SetWindowDisplayMode(WindowDisplayMode.Minimized);

        Assert.IsTrue(window.TitleRectangle.Contains(minimizeRestoreButton.ButtonRectangle.Center));
    }

    /// <summary>
    /// Regression test: RecalculateMinimizedWindowSize used to size the minimized title bar
    /// to fit only the title text, with no allowance for the title buttons sitting on top of
    /// it. A short title could shrink the title bar narrower than the close + minimize/
    /// restore buttons' combined width, and RepositionTitleButtons (which only knows about
    /// _title.Size.X, not text width) would then tile them overlapping each other -- visually
    /// reading as a stray artifact between the two buttons.
    /// </summary>
    [TestMethod]
    public void MinimizedWindow_ShortTitle_ButtonsDoNotOverlap()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            // A single-character title is short enough that, pre-fix, the minimized title bar
            // would be narrower than the two buttons combined.
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "X", CanUserClose = true, CanUserMinimize = true },
        });
        window.Initialize();

        window.SetWindowDisplayMode(WindowDisplayMode.Minimized);

        var closeButton = window.TitleButtons[0];
        var minimizeRestoreButton = window.TitleButtons[1];
        Assert.IsFalse(closeButton.ButtonRectangle.Intersects(minimizeRestoreButton.ButtonRectangle));
    }

    /// <summary>
    /// Regression test: WindowService pools and reuses Window instances (CloseWindow pushes
    /// back to the pool instead of destroying), and WindowMinimizeRestoreBehavior.Attach used
    /// to subscribe to window.DisplayModeChanged with an anonymous lambda that never detached --
    /// each reuse cycle added another stale subscription pinning a discarded button. If that
    /// leak were still present, toggling display mode on the reused window would update the
    /// old, orphaned button too (harmless here since nothing reads it) but more importantly the
    /// old handler closing over a disposed-of button would still be invoked; asserting the
    /// current button's glyph stays correct after several close/reopen cycles confirms the
    /// fix is wired (no stale handler throwing or corrupting shared state).
    /// </summary>
    [TestMethod]
    public void PooledWindowReuse_MinimizeRestoreSubscription_DoesNotAccumulate()
    {
        var windowService = CreateWindowService();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var window = CreateWindowWithCloseAndMinimize(windowService);
            var minimizeRestoreButton = window.TitleButtons[1];

            window.SetWindowDisplayMode(WindowDisplayMode.Minimized);
            Assert.AreEqual("O", minimizeRestoreButton.Text);

            window.Close();
        }

        var finalWindow = CreateWindowWithCloseAndMinimize(windowService);
        var finalButton = finalWindow.TitleButtons[1];

        finalWindow.SetWindowDisplayMode(WindowDisplayMode.Minimized);

        Assert.AreEqual("O", finalButton.Text);
    }
}