using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Chrome;

namespace Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class ScrollbarLayoutTests
{
    private const float RowHeight = 20f;

    private static ElementPoolService CreateWindowService() => TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());

    private static Window CreateColumn(ElementPoolService windowService, Element? parent, Vector2 size, ScrollbarVisibility? scrollbarVisibility = null)
    {
        var column = windowService.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true, ScrollbarVisibility = scrollbarVisibility },
        });

        if (parent is null)
        {
            column.Initialize();
        }
        else
        {
            parent.AddChild(column);
        }

        return column;
    }

    private static TextWindow AddRow(ElementPoolService windowService, Window column, int index)
    {
        var row = windowService.CreateElement<TextWindow>(column, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(column.ContentSize.X, RowHeight), MaximumSize = new Vector2(column.ContentSize.X, 10_000), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
            Text = new TextOptions { Text = $"Row {index}" },
        });
        column.AddChild(row);
        return row;
    }

    private static void AddRows(ElementPoolService windowService, Window column, int rowCount)
    {
        for (var index = 0; index < rowCount; index++)
        {
            AddRow(windowService, column, index);
        }
    }

    [TestMethod]
    public void ContentThatFits_ShowsNoScrollbar_AndKeepsFullContentWidth()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 200));
        var contentWidthBeforeRows = column.ContentSize.X;

        AddRows(windowService, column, rowCount: 3);

        Assert.IsFalse(column.ShowVerticalScrollbar);
        Assert.AreEqual(contentWidthBeforeRows, column.ContentSize.X);
        Assert.AreEqual(Rectangle.Empty, column.VerticalScrollbarTrackRectangle);
    }

    [TestMethod]
    public void OverflowingContent_ShowsVerticalScrollbar_AndReservesItsGutter()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 100));
        var contentWidthBeforeRows = column.ContentSize.X;

        AddRows(windowService, column, rowCount: 20);

        Assert.IsTrue(column.ShowVerticalScrollbar);
        Assert.IsFalse(column.ShowHorizontalScrollbar);
        Assert.AreEqual(contentWidthBeforeRows - WindowChrome.ScrollbarThickness, column.ContentSize.X);
    }

    [TestMethod]
    public void OverflowingContent_RowsShrinkToTheNarrowerContentWidth()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 100));

        AddRows(windowService, column, rowCount: 20);

        foreach (var row in column.ChildElements)
        {
            Assert.AreEqual(column.ContentSize.X, row.CurrentSize.X);
        }
    }

    [TestMethod]
    public void RemovingRowsUntilContentFits_HidesTheScrollbar_AndRestoresContentWidth()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 100));
        var contentWidthBeforeRows = column.ContentSize.X;
        AddRows(windowService, column, rowCount: 20);

        foreach (var row in column.ChildElements.Skip(2).ToArray())
        {
            column.RemoveChild(row.ElementId);
        }

        Assert.IsFalse(column.ShowVerticalScrollbar);
        Assert.AreEqual(contentWidthBeforeRows, column.ContentSize.X);
    }

    [TestMethod]
    public void HiddenVisibility_OverflowingContent_StillScrollsButReservesNothing()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 100), ScrollbarVisibility.Hidden);
        var contentWidthBeforeRows = column.ContentSize.X;

        AddRows(windowService, column, rowCount: 20);

        Assert.IsFalse(column.ShowVerticalScrollbar);
        Assert.AreEqual(contentWidthBeforeRows, column.ContentSize.X);
        Assert.IsGreaterThan(0f, column.MaxScrollOffset.Y);
    }

    [TestMethod]
    public void ScrollbarAppearing_RaisesContentResized_WithoutResized()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 100));
        var contentResizedCount = 0;
        var resizedCount = 0;
        column.ContentResized += _ => contentResizedCount++;
        column.Resized += _ => resizedCount++;

        AddRows(windowService, column, rowCount: 20);

        Assert.AreEqual(1, contentResizedCount);
        Assert.AreEqual(0, resizedCount);
    }

    [TestMethod]
    public void LayoutBatch_ScrollbarSettlesOnceAtTheEnd()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 100));
        var contentResizedCount = 0;
        column.ContentResized += _ => contentResizedCount++;

        using (column.BeginLayoutBatch())
        {
            AddRows(windowService, column, rowCount: 20);

            Assert.IsFalse(column.ShowVerticalScrollbar);
        }

        Assert.IsTrue(column.ShowVerticalScrollbar);
        Assert.AreEqual(1, contentResizedCount);
    }

    [TestMethod]
    public void ShrinkingOuterWindow_ColumnScrollbarAppears_GrowingItBackHidesIt()
    {
        var windowService = CreateWindowService();
        var outer = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(300, 300), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserResize = true },
        });
        outer.Initialize();
        var column = CreateColumn(windowService, outer, outer.ContentSize);
        AddRows(windowService, column, rowCount: 10);
        Assert.IsFalse(column.ShowVerticalScrollbar);

        outer.SetBounds(outer.RelativePosition, new Vector2(300, 100));
        column.SetBounds(Vector2.Zero, outer.ContentSize);
        Assert.IsTrue(column.ShowVerticalScrollbar);

        outer.SetBounds(outer.RelativePosition, new Vector2(300, 300));
        column.SetBounds(Vector2.Zero, outer.ContentSize);
        Assert.IsFalse(column.ShowVerticalScrollbar);
    }

    [TestMethod]
    public void NestedScrollables_OnlyTheOverflowingInnerOneShowsABar()
    {
        var windowService = CreateWindowService();
        var body = CreateColumn(windowService, null, new Vector2(200, 200));
        var grid = CreateColumn(windowService, body, body.ContentSize);

        AddRows(windowService, grid, rowCount: 30);

        Assert.IsTrue(grid.ShowVerticalScrollbar);
        Assert.IsFalse(body.ShowVerticalScrollbar);
    }

    [TestMethod]
    public void VerticalTrack_RunsDownTheRightEdgeOfTheContentBackground()
    {
        var windowService = CreateWindowService();
        var column = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(50, 60), Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserScrollVertical = true },
        });
        column.Initialize();

        AddRows(windowService, column, rowCount: 20);

        var track = column.VerticalScrollbarTrackRectangle;
        Assert.AreEqual(column.Rectangle.Right - 1, track.Right);
        Assert.AreEqual(column.Rectangle.Top + 1, track.Top);
        Assert.AreEqual(column.Rectangle.Bottom - 1, track.Bottom);
        Assert.AreEqual((int)WindowChrome.ScrollbarThickness, track.Width);
        Assert.IsLessThanOrEqualTo(track.Left, column.ContentRectangle.Right);
    }

    [TestMethod]
    public void ScrollingToTheEnd_MovesTheThumbToTheBottomOfTheTrack()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 100));
        AddRows(windowService, column, rowCount: 20);
        Assert.AreEqual(column.VerticalScrollbarTrackRectangle.Top, column.VerticalScrollbarThumbRectangle.Top);

        column.ScrollBy(new Vector2(0, column.MaxScrollOffset.Y));

        Assert.AreEqual(column.VerticalScrollbarTrackRectangle.Bottom, column.VerticalScrollbarThumbRectangle.Bottom);
    }

    [TestMethod]
    public void ThumbSpan_LengthIsTheVisibleFractionOfTheTrack()
    {
        var (start, length) = Element.ComputeScrollbarThumbSpan(trackStart: 10, trackLength: 200, visibleLength: 100, maxScrollOffset: 300, scrollOffset: 0);

        Assert.AreEqual(10, start);
        Assert.AreEqual(50, length);
    }

    [TestMethod]
    public void ThumbSpan_HalfwayScrolled_SitsHalfwayAlongTheRemainingTrack()
    {
        var (start, length) = Element.ComputeScrollbarThumbSpan(trackStart: 10, trackLength: 200, visibleLength: 100, maxScrollOffset: 300, scrollOffset: 150);

        Assert.AreEqual(10 + (200 - length) / 2, start);
    }

    [TestMethod]
    public void ThumbSpan_VeryLongContent_NeverShorterThanTheMinimumLength()
    {
        var (_, length) = Element.ComputeScrollbarThumbSpan(trackStart: 0, trackLength: 200, visibleLength: 100, maxScrollOffset: 1_000_000, scrollOffset: 0);

        Assert.AreEqual((int)WindowChrome.ScrollbarMinimumThumbLength, length);
    }

    [TestMethod]
    public void ThumbSpan_TrackShorterThanTheMinimumLength_FillsTheTrack()
    {
        var (_, length) = Element.ComputeScrollbarThumbSpan(trackStart: 0, trackLength: 10, visibleLength: 5, maxScrollOffset: 1_000, scrollOffset: 0);

        Assert.AreEqual(10, length);
    }

    [TestMethod]
    public void FixedTextWindow_TextOverflowing_ShowsBarAndRewrapsToTheNarrowerWidth()
    {
        var windowService = CreateWindowService();
        var text = string.Join(" ", Enumerable.Repeat("word", 400));
        var window = windowService.CreateElement<TextWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Text = text },
        });
        window.Initialize();

        Assert.IsTrue(window.ShowVerticalScrollbar);
        Assert.IsLessThanOrEqualTo(window.ContentSize.X, TextWidestLineWidth(window));
    }

    [TestMethod]
    public void FixedTextWindow_UpdateTextPastTheEnd_ShowsBar_AndBackToShortTextHidesIt()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateElement<TextWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Text = "Short" },
        });
        window.Initialize();
        Assert.IsFalse(window.ShowVerticalScrollbar);

        window.UpdateText(string.Join(" ", Enumerable.Repeat("word", 400)));
        Assert.IsTrue(window.ShowVerticalScrollbar);

        window.UpdateText("Short");
        Assert.IsFalse(window.ShowVerticalScrollbar);
    }

    [TestMethod]
    public void WrapContentTextWindow_CappedHeight_AddsTheGutterWithinItsMaximumWidth()
    {
        var windowService = CreateWindowService();
        var text = string.Join(Environment.NewLine, Enumerable.Range(1, 100).Select(n => $"Line {n}"));
        var window = windowService.CreateElement<TextWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(300, 100), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Text = text },
        });
        window.Initialize();

        Assert.IsTrue(window.ShowVerticalScrollbar);
        Assert.IsLessThanOrEqualTo(300f, window.CurrentSize.X);
        Assert.AreEqual(window.ContentSize.X + WindowChrome.ScrollbarThickness, window.CurrentSize.X, 0.01f);
    }

    [TestMethod]
    public void PooledReuse_WithoutOverflow_CarriesNoScrollbarFromItsPreviousLife()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(200, 100));
        AddRows(windowService, column, rowCount: 20);
        Assert.IsTrue(column.ShowVerticalScrollbar);
        column.Close();

        var reused = CreateColumn(windowService, null, new Vector2(200, 100));

        Assert.IsFalse(reused.ShowVerticalScrollbar);
        Assert.AreEqual(Rectangle.Empty, reused.VerticalScrollbarTrackRectangle);
    }

    private static float TextWidestLineWidth(TextWindow window) =>
        window.DisplayText.FormattedText.Split('\n').Max(line => window.ContentFont.MeasureString(line).X);
}
