using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Regression tests for TextWindow's WrapContent sizing: a root WrapContent TextWindow (e.g.
/// a notification popup) used to always claim its full MaximumSize.X as its width, using that
/// same value only as the word-wrap boundary rather than the window's actual rendered size --
/// so short text (well under the wrap boundary) still produced a needlessly wide window.
/// RecalculateWrapContentWindowSize now wraps against the maximum first, then shrinks the
/// window down to whatever the widest wrapped line actually needs.
/// </summary>
[TestClass]
public sealed class TextWindowSizingTests
{
    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    private static TextWindow CreateWrapContentTextWindow(WindowService windowService, string text, Vector2 maximumSize)
    {
        return windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(200, 200), MaximumSize = maximumSize, DisplayMode = WindowDisplayMode.WrapContent },
            Chrome = new WindowChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "Test" },
            Text = new TextOptions { Text = text },
        });
    }

    [TestMethod]
    public void ShortText_ShrinksNarrowerThanMaximumSize()
    {
        var windowService = CreateWindowService();
        var window = CreateWrapContentTextWindow(windowService, "Hi", new Vector2(400, 300));

        window.Initialize();

        Assert.IsLessThan(400, window.WindowCurrentSize.X);
    }

    [TestMethod]
    public void LongText_WrapsAndNeverExceedsMaximumSize()
    {
        var windowService = CreateWindowService();
        var longText = string.Join(' ', Enumerable.Repeat("word", 100));
        var window = CreateWrapContentTextWindow(windowService, longText, new Vector2(400, 300));

        window.Initialize();

        // Word-wrap leaves some per-line slack (it breaks on word boundaries, not exactly at
        // the boundary), so the shrunk width lands close to but not exactly at 400 -- the
        // guarantee that matters is the clamp (never wider than the maximum) plus actually
        // using most of the available width (not collapsing back to the short-text case).
        Assert.IsLessThanOrEqualTo(400, window.WindowCurrentSize.X);
        Assert.IsGreaterThan(300, window.WindowCurrentSize.X);
    }

    /// <summary>Regression guard for the drag bug: a root window's shrink-to-fit width must not depend on its screen position.</summary>
    [TestMethod]
    public void ShortText_WidthIsUnaffectedByRelativePosition()
    {
        var windowService = CreateWindowService();
        var window = CreateWrapContentTextWindow(windowService, "Hi", new Vector2(400, 300));
        window.Initialize();
        var widthBeforeMove = window.WindowCurrentSize.X;

        window.SetRelativePosition(new Vector2(350, 200));

        Assert.AreEqual(widthBeforeMove, window.WindowCurrentSize.X);
    }
}
