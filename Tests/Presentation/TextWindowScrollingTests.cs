using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Covers TextWindow's scroll support (Window.ScrollBy/MaxScrollOffset, gated by
/// CanUserScrollVertical) -- Fixed/Fill only, since WrapContent always sizes itself exactly to
/// its own text (see RecalculateWrapContentWindowSize) and so never has anything to scroll (see
/// TextWindow.UpdateScrollBounds, which only Fixed/Fill call).
/// </summary>
[TestClass]
public sealed class TextWindowScrollingTests
{
    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    private static TextWindow CreateFixedTextWindow(WindowService windowService, string text, Vector2 size, bool canUserScrollVertical)
    {
        var window = windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = size, MaximumSize = new Vector2(size.X, 1000), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserScrollVertical = canUserScrollVertical },
            Text = new TextOptions { Text = text },
        });
        window.Initialize();
        return window;
    }

    private static string LongText() => string.Join(' ', Enumerable.Repeat("word", 200));

    [TestMethod]
    public void OverflowingText_WithScrollEnabled_HasPositiveMaxScrollOffset()
    {
        var windowService = CreateWindowService();
        var window = CreateFixedTextWindow(windowService, LongText(), new Vector2(150, 30), canUserScrollVertical: true);

        Assert.IsGreaterThan(0, window.MaxScrollOffset.Y);
    }

    [TestMethod]
    public void ShortText_NeverHasAnyMaxScrollOffset()
    {
        var windowService = CreateWindowService();
        var window = CreateFixedTextWindow(windowService, "Hi", new Vector2(150, 100), canUserScrollVertical: true);

        Assert.AreEqual(0f, window.MaxScrollOffset.Y);
    }

    [TestMethod]
    public void ScrollBy_ClampsToMaxScrollOffset()
    {
        var windowService = CreateWindowService();
        var window = CreateFixedTextWindow(windowService, LongText(), new Vector2(150, 30), canUserScrollVertical: true);

        window.ScrollBy(new Vector2(0, 100_000));

        Assert.AreEqual(window.MaxScrollOffset.Y, window.ScrollOffset.Y);
    }

    [TestMethod]
    public void ScrollBy_NeverGoesNegative()
    {
        var windowService = CreateWindowService();
        var window = CreateFixedTextWindow(windowService, LongText(), new Vector2(150, 30), canUserScrollVertical: true);

        window.ScrollBy(new Vector2(0, -100_000));

        Assert.AreEqual(0f, window.ScrollOffset.Y);
    }

    /// <summary>
    /// Regression guard: overflowing text alone isn't enough to make a window scrollable --
    /// CanUserScrollVertical must actually be set, or ScrollBy stays a no-op regardless of how
    /// much content there is to scroll through.
    /// </summary>
    [TestMethod]
    public void ScrollBy_WithoutCanUserScrollVertical_DoesNothing()
    {
        var windowService = CreateWindowService();
        var window = CreateFixedTextWindow(windowService, LongText(), new Vector2(150, 30), canUserScrollVertical: false);

        window.ScrollBy(new Vector2(0, 50));

        Assert.AreEqual(0f, window.ScrollOffset.Y);
    }

    /// <summary>Growing a window (shrinking how much it has to scroll) must re-clamp wherever it was already scrolled to, not leave ScrollOffset pointing past the new, smaller MaxScrollOffset.</summary>
    [TestMethod]
    public void GrowingAWindow_ReClampsScrollOffsetToTheNewMaxScrollOffset()
    {
        var windowService = CreateWindowService();
        var window = CreateFixedTextWindow(windowService, LongText(), new Vector2(150, 30), canUserScrollVertical: true);
        window.ScrollBy(new Vector2(0, window.MaxScrollOffset.Y));

        window.SetSize(new Vector2(150, 300));

        Assert.IsLessThanOrEqualTo(window.MaxScrollOffset.Y, window.ScrollOffset.Y);
    }

    /// <summary>
    /// Regression guard for a bug found via manual testing: MaxScrollOffset must reflect
    /// DrawContent's actual single-DrawString-call rendering (FontStashSharp spaces
    /// newline-separated lines using the font's own LineHeight advance, with nothing extra
    /// between them -- see TextWindow.TextContentHeight), not an inflated estimate that adds
    /// a full LinePadding gap between every line. The inflated estimate let scrolling run
    /// roughly one window's height past the real end of the text -- harmless for an
    /// auto-sized WrapContent window pre-scrolling (just some extra blank space at the
    /// bottom), but very visible once MaxScrollOffset started depending on the same estimate.
    /// </summary>
    [TestMethod]
    public void WrapContentClampedByMaximumSize_MaxScrollOffsetMatchesActualLineSpacing()
    {
        var windowService = CreateWindowService();
        var lineCount = 100;
        var text = string.Join(Environment.NewLine, Enumerable.Range(1, lineCount).Select(n => $"Line {n}"));
        var window = windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { MaximumSize = new Vector2(300, 100), DisplayMode = WindowDisplayMode.WrapContent },
            Chrome = new WindowChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Text = text },
        });
        window.Initialize();

        // LinePadding is 3 (TextWindow's own private constant, mirrored here) -- once at the
        // top (matching DrawContent's drawing origin) and once at the bottom, not once per
        // line (the bug: LinePadding * (lineCount + 1), i.e. a gap between every line).
        var expectedTextHeight = window.ContentFont.LineHeight * window.DisplayText.LineCount + 3 * 2;
        var expectedMaxScrollY = expectedTextHeight - window.ContentSize.Y;

        Assert.AreEqual(expectedMaxScrollY, window.MaxScrollOffset.Y, 0.01f);
    }

    /// <summary>
    /// Regression test for the selection-window overlap fix: a plain (non-TextWindow) parent
    /// with CanUserScrollVertical set must get a positive MaxScrollOffset once its
    /// WrapContent-height children's combined extent exceeds its own fixed content size --
    /// AddChildWindow's RecalculateScrollBoundsFromChildren is what wires this up, mirroring
    /// how a WrapContent parent already sizes itself around its children.
    /// </summary>
    [TestMethod]
    public void ParentWindow_ChildrenExceedContentSize_GetsPositiveMaxScrollOffset()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 10), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserScrollVertical = true },
        });
        parent.Initialize();

        for (var index = 0; index < 3; index++)
        {
            var child = windowService.CreateWindow<TextWindow>(parent, new WindowOptions
            {
                Layout = new WindowLayoutOptions { MaximumSize = new Vector2(parent.ContentSize.X, 1000), DisplayMode = WindowDisplayMode.WrapContent },
                Text = new TextOptions { Text = $"Child {index}" },
            });
            parent.AddChildWindow(child);
        }

        Assert.IsGreaterThan(0, parent.MaxScrollOffset.Y);
    }

    /// <summary>
    /// Regression test: scrolling a parent must actually move its children's absolute screen
    /// position (RecalculateAbsolutePositions subtracts the parent's ScrollOffset), not just
    /// update ScrollOffset/MaxScrollOffset bookkeeping with no visible effect -- Window.Draw's
    /// child-window loop positions children by WindowAbsolutePosition, not through the
    /// CameraTransform viewport pass DrawContent uses, so this is the only mechanism that
    /// actually scrolls child windows at all (see ScrollBy's call to Arrange()).
    /// </summary>
    [TestMethod]
    public void ScrollBy_OnParentWindow_ShiftsChildAbsolutePosition()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 10), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserScrollVertical = true },
        });
        parent.Initialize();

        Window? firstChild = null;
        for (var index = 0; index < 3; index++)
        {
            var child = windowService.CreateWindow<TextWindow>(parent, new WindowOptions
            {
                Layout = new WindowLayoutOptions { MaximumSize = new Vector2(parent.ContentSize.X, 1000), DisplayMode = WindowDisplayMode.WrapContent },
                Text = new TextOptions { Text = $"Child {index}" },
            });
            parent.AddChildWindow(child);
            firstChild ??= child;
        }

        var positionBeforeScroll = firstChild!.WindowAbsolutePosition;

        parent.ScrollBy(new Vector2(0, parent.MaxScrollOffset.Y));

        Assert.AreEqual(positionBeforeScroll.Y - parent.MaxScrollOffset.Y, firstChild.WindowAbsolutePosition.Y, 0.01f);
    }
}
