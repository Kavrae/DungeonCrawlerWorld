using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Confirms Element.FooterHeight/Window.SetFooterContent reserve real, correctly-sized/positioned
/// room at the bottom of a window's content area -- see TODO.md's "Element footer" item.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowFooterTests
{
    private const float FooterHeight = 24f;

    private static ElementPoolService CreateWindowService() => TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());

    /// <summary>Trivial IElementContent stub -- records the hostWindow it was given so a test can inspect its ContentSize/AbsolutePosition/Rectangle directly.</summary>
    private sealed class StubFooterContent : IElementContent
    {
        public Window? HostWindow { get; private set; }
        public void Initialize(Window hostWindow) => HostWindow = hostWindow;
        public void Update(GameTime gameTime) { }
        public void DrawContent(GameTime gameTime) { }
    }

    private static Window CreateWindowWithFooter(ElementPoolService windowService, StubFooterContent footerContent, Vector2? size = null)
    {
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = size ?? new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        window.SetFooterContent(footerContent, FooterHeight);
        window.Initialize();
        return window;
    }

    [TestMethod]
    public void SetFooterContent_FixedMode_ShrinksContentSizeByFooterHeight()
    {
        var plainWindow = CreateWindowService().CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        plainWindow.Initialize();
        var unfooteredContentHeight = plainWindow.ContentSize.Y;

        var footeredWindow = CreateWindowWithFooter(CreateWindowService(), new StubFooterContent());

        Assert.AreEqual(unfooteredContentHeight - FooterHeight, footeredWindow.ContentSize.Y);
    }

    /// <summary>
    /// Regression test for the confirmed live bug: the footer host window used to be positioned
    /// exactly at RelativePosition.Y == ContentSize.Y, which made Element.Measure's shared
    /// child-sizing formula (parentContentSize - child.RelativePosition) self-cancel to zero --
    /// collapsing the whole footer to a degenerate zero-height rectangle regardless of its own
    /// requested FooterHeight.
    /// </summary>
    [TestMethod]
    public void SetFooterContent_FooterHostWindow_HasRealNonZeroHeight()
    {
        var footerContent = new StubFooterContent();
        var window = CreateWindowWithFooter(CreateWindowService(), footerContent);

        Assert.IsNotNull(footerContent.HostWindow, "SetFooterContent's content should have been Initialize()'d against a real host window.");
        Assert.AreEqual(FooterHeight, footerContent.HostWindow!.CurrentSize.Y);
        Assert.AreEqual(window.ContentSize.X, footerContent.HostWindow.CurrentSize.X);
    }

    [TestMethod]
    public void SetFooterContent_FooterHostWindow_SitsFlushBelowMainContent()
    {
        var footerContent = new StubFooterContent();
        var window = CreateWindowWithFooter(CreateWindowService(), footerContent);

        var footerHost = footerContent.HostWindow!;

        Assert.AreEqual(window.ContentAbsolutePosition.Y + window.ContentSize.Y, footerHost.AbsolutePosition.Y);
        Assert.AreEqual(window.ContentAbsolutePosition.X, footerHost.AbsolutePosition.X);
    }

    /// <summary>
    /// Z-order regression check: the footer host must be the LAST entry in the outer window's own
    /// ChildElements (drawn last == on top, and checked first by TryHitTestInteraction's
    /// topmost-first walk) relative to whatever else was added before SetFooterContent's own
    /// OnChildrenInitialized ran -- an earlier-added, wrongly-sized sibling landing after it would
    /// silently draw over / swallow hover and clicks meant for the footer.
    /// </summary>
    [TestMethod]
    public void SetFooterContent_FooterHostWindow_IsLastChild_DrawsOnTopAndWinsHitTest()
    {
        var footerContent = new StubFooterContent();
        var windowService = CreateWindowService();
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        window.SetFooterContent(footerContent, FooterHeight);
        window.Initialize();

        Assert.AreSame(footerContent.HostWindow, window.ChildElements[^1], "Footer host window should be the last (topmost) child.");

        // TryHitTestInteraction is internal, not public -- reachable here via Presentation's
        // InternalsVisibleTo("Tests").
        var hit = window.TryHitTestInteraction(new Point((int)footerContent.HostWindow!.AbsolutePosition.X + 1, (int)footerContent.HostWindow.AbsolutePosition.Y + 1));
        Assert.AreSame(footerContent.HostWindow, hit.Element, "A point inside the footer band should hit-test to the footer host window, not a sibling drawn later/positioned over it.");
    }

    [TestMethod]
    public void PooledWindowReuse_FooterHeight_DoesNotAccumulate()
    {
        var windowService = CreateWindowService();
        var footerContent = new StubFooterContent();
        var window = CreateWindowWithFooter(windowService, footerContent);
        window.Close();

        var reused = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        reused.Initialize();

        Assert.AreEqual(0f, reused.FooterHeight);
        Assert.IsFalse(reused.ShowFooter);
        Assert.IsNull(reused.FooterContent);
    }
}
