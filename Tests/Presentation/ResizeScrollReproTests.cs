using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Regression coverage for a bug found via manual testing: resizing a Fixed-size window whose
/// scrollable child holds several Fixed-height rows shrank the rows themselves instead of just
/// changing how much of them was visible. Root cause was in Element.cs, not any specific window --
/// MeasureAndArrange's own "a scrollable parent lets its children keep their own configured
/// MaximumSize on the scroll axis" exemption (see ComputeChildAvailableSize) was only applied when
/// MeasureAndArrange was called directly on a child; a cascading remeasure triggered by an ancestor
/// resizing went through MeasureChildren instead, which called child.Measure(...) directly with no
/// exemption at all, silently reclamping every row's height to the parent's own (now smaller)
/// content size. Confirmed live via HealthWindow's own two resizable, independently-scrolling
/// columns (body part rows visibly shrinking/overlapping after a window resize).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ResizeScrollReproTests
{
    private static ElementPoolService CreateWindowService() => TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());

    private static (Window Outer, Window Column) CreateResizableOuterWithScrollableColumn(ElementPoolService windowService, int rowCount, float rowHeight)
    {
        var outer = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(300, 300), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserResize = true },
        });
        outer.Initialize();

        var column = windowService.CreateElement<Window>(outer, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = outer.ContentSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = true },
        });
        outer.AddChild(column);

        for (var index = 0; index < rowCount; index++)
        {
            var row = windowService.CreateElement<TextWindow>(column, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(column.ContentSize.X, rowHeight), MaximumSize = new Vector2(column.ContentSize.X, 10_000), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
                Text = new TextOptions { Text = $"Row {index}" },
            });
            column.AddChild(row);
        }

        return (outer, column);
    }

    [TestMethod]
    public void ShrinkingOuterWindow_RowsInsideScrollableColumn_KeepTheirOwnConfiguredHeight()
    {
        var windowService = CreateWindowService();
        var (outer, column) = CreateResizableOuterWithScrollableColumn(windowService, rowCount: 20, rowHeight: 20);
        var firstRow = column.ChildElements.First();

        // Mirrors UiInputController.HandleMouseDrag's own resize call, then HealthWindow's own
        // HandleResized reflowing the (single, here) column to the outer window's new content size.
        outer.SetBounds(outer.RelativePosition, new Vector2(300, 100));
        column.SetBounds(Vector2.Zero, outer.ContentSize);

        Assert.AreEqual(20f, firstRow.CurrentSize.Y, "A row's own configured height must survive an ancestor's resize -- only how much of the column is visible should change, not the rows themselves.");
    }

    [TestMethod]
    public void ShrinkingOuterWindow_InnerScrollableColumn_MaxScrollOffsetIncreases()
    {
        var windowService = CreateWindowService();
        var (outer, column) = CreateResizableOuterWithScrollableColumn(windowService, rowCount: 20, rowHeight: 20);
        var maxScrollBeforeResize = column.MaxScrollOffset.Y;

        outer.SetBounds(outer.RelativePosition, new Vector2(300, 100));
        column.SetBounds(Vector2.Zero, outer.ContentSize);

        Assert.IsGreaterThan(maxScrollBeforeResize, column.MaxScrollOffset.Y);
    }

    [TestMethod]
    public void GrowingOuterWindow_InnerScrollableColumn_MaxScrollOffsetDecreases()
    {
        var windowService = CreateWindowService();
        var (outer, column) = CreateResizableOuterWithScrollableColumn(windowService, rowCount: 20, rowHeight: 20);
        outer.SetBounds(outer.RelativePosition, new Vector2(300, 100));
        column.SetBounds(Vector2.Zero, outer.ContentSize);
        var maxScrollAfterShrink = column.MaxScrollOffset.Y;

        outer.SetBounds(outer.RelativePosition, new Vector2(300, 300));
        column.SetBounds(Vector2.Zero, outer.ContentSize);

        Assert.IsLessThan(maxScrollAfterShrink, column.MaxScrollOffset.Y);
    }

    [TestMethod]
    public void ShrinkingOuterWindow_ScrolledToBottom_StillReachesTheLastRow()
    {
        var windowService = CreateWindowService();
        var (outer, column) = CreateResizableOuterWithScrollableColumn(windowService, rowCount: 20, rowHeight: 20);

        outer.SetBounds(outer.RelativePosition, new Vector2(300, 100));
        column.SetBounds(Vector2.Zero, outer.ContentSize);
        column.ScrollBy(new Vector2(0, column.MaxScrollOffset.Y));

        var lastRow = column.ChildElements.Last();
        var lastRowBottomRelativeToColumn = lastRow.RelativePosition.Y + lastRow.CurrentSize.Y - column.ScrollOffset.Y;

        Assert.AreEqual(column.ContentSize.Y, lastRowBottomRelativeToColumn, 0.01f);
    }
}
