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

    /// <summary>
    /// Regression test for the reported bug: SelectionWindowContent tiles one TextWindow per
    /// component vertically under a Fixed-height parent (see GameShellBootstrapper's
    /// selectionWindow), each child's own MaximumSize set to the *whole* parent content size
    /// (not "whatever's left"), per RecalculateWrapContentWindowSize's own
    /// maximumContentHeight = MaximumSize.Y - RelativePosition.Y. Enough long-text siblings in
    /// a row can push a later one's RelativePosition.Y past MaximumSize.Y outright, which used
    /// to make maximumContentHeight -- and so that child's own CurrentSize.Y -- go negative.
    /// RetileChildrenFrom chains each sibling off the previous one's
    /// RelativePosition.Y + CurrentSize.Y, so a negative CurrentSize.Y made the next sibling
    /// after it jump backward (overlapping something earlier) instead of continuing downward.
    /// Reproduced with a goblin engineer (long component text) and dirt (terrain) sharing a
    /// map tile: dirt's own component windows ended up overlapping each other.
    /// </summary>
    [TestMethod]
    public void SiblingsExceedingParentBudget_NeverJumpBackwardInsteadOfContinuingDownward()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            // Deliberately short -- a handful of long-text children will together exceed this
            // easily, the same way several component windows can exceed selectionWindow's
            // real 744px budget.
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 100), DisplayMode = WindowDisplayMode.Fixed },
        });
        parent.Initialize();

        var longText = string.Join(' ', Enumerable.Repeat("word", 60));
        Window? previousChild = null;

        // Four long-text siblings: by the third or fourth, RelativePosition.Y (chained off
        // each predecessor) has already exceeded the parent's 100px MaximumSize.Y budget.
        for (var index = 0; index < 4; index++)
        {
            var child = windowService.CreateWindow<TextWindow>(parent, new WindowOptions
            {
                Layout = new WindowLayoutOptions { MaximumSize = parent.ContentSize, DisplayMode = WindowDisplayMode.WrapContent },
                Text = new TextOptions { Text = longText },
            });
            parent.AddChildWindow(child);

            Assert.IsGreaterThanOrEqualTo(0, child.WindowCurrentSize.Y, $"Sibling {index}'s own height went negative.");
            if (previousChild is not null)
            {
                Assert.IsGreaterThanOrEqualTo(
                    previousChild.WindowRelativePosition.Y,
                    child.WindowRelativePosition.Y,
                    $"Sibling {index} landed above sibling {index - 1} instead of at or below it.");
            }

            previousChild = child;
        }
    }

    /// <summary>
    /// Regression test for the reported bug. Asserts the *geometric* non-overlap condition
    /// (the button's own left edge starts at or after where the text visually ends) rather
    /// than comparing TitleSize.X against a hand-computed threshold -- an earlier version of
    /// this test did the latter and passed even against the unfixed code, because
    /// TitlePadding.X*2 happened to equal this test's default button width for this font,
    /// masking the bug instead of catching it.
    /// </summary>
    [TestMethod]
    public void WrapContentWindow_TitleAndCloseButton_BothFitWithoutOverlapping()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            // Short body text -- the title bar (text + button), not the content, must be what
            // drives this window's width for the bug to actually get exercised.
            Layout = new WindowLayoutOptions { MaximumSize = new Vector2(640, 300), DisplayMode = WindowDisplayMode.WrapContent },
            Chrome = new WindowChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "New Quest (Enter to submit)", CanUserClose = true },
            Text = new TextOptions { Text = "Hi" },
        });

        window.Initialize();

        var closeButton = window.TitleButtons[0];
        var titleTextWidth = window.TitleFont.MeasureString(window.TitleText).X;

        Assert.IsGreaterThanOrEqualTo(window.TitlePadding.X + titleTextWidth, closeButton.RelativePosition.X);
    }

    /// <summary>
    /// Regression test for the actual root cause: MinimumTitleWidth is only ever evaluated
    /// during Initialize's own first measure -- before any button exists, whether built-in
    /// (CanUserClose, attached inside Initialize but still after that first measure) or,
    /// like the real notification popup's NotificationMinimizeBehavior, attached externally
    /// well after Initialize has already returned. AddTitleButton itself must trigger a
    /// re-measure for a WrapContent window, not just reposition buttons within whatever width
    /// happened to be computed before any of them existed.
    /// </summary>
    [TestMethod]
    public void WrapContentWindow_ButtonAddedAfterInitialize_StillFitsWithoutOverlapping()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { MaximumSize = new Vector2(640, 300), DisplayMode = WindowDisplayMode.WrapContent },
            Chrome = new WindowChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "New Quest (Enter to submit)" },
            Text = new TextOptions { Text = "Hi" },
        });
        window.Initialize();

        var extraButton = new Button(window, new ButtonOptions { Text = "_" });
        window.AddTitleButton(extraButton);

        var titleTextWidth = window.TitleFont.MeasureString(window.TitleText).X;
        Assert.IsGreaterThanOrEqualTo(window.TitlePadding.X + titleTextWidth, extraButton.RelativePosition.X);
    }
}
