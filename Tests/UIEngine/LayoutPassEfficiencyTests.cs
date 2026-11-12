using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class LayoutPassEfficiencyTests
{
    private const float RowHeight = 20f;

    private static ElementPoolService CreatePool() => TestElementPoolServiceFactory.Create(TestFonts.Shared);

    private static Window CreateContainer(ElementPoolService pool, Element? parent, Vector2 size, ElementDisplayMode displayMode = ElementDisplayMode.Fixed, ChildElementTileMode tileMode = ChildElementTileMode.Vertical, bool scrollsVertically = false)
    {
        var container = pool.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = tileMode },
            Layout = new ElementLayoutOptions { Size = size, DisplayMode = displayMode },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserScrollVertical = scrollsVertically },
        });

        if (parent is null)
        {
            container.Initialize();
        }
        else
        {
            parent.AddChild(container);
        }

        return container;
    }

    private static TextWindow AddTextRow(ElementPoolService pool, Element parent, string text, float width = 200f)
    {
        var row = pool.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(width, RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
            Text = new TextOptions { Text = text },
        });
        parent.AddChild(row);
        return row;
    }

    [TestMethod]
    public void CloseAllChildren_LeavesNoChildInTheParent()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 400));
        for (var index = 0; index < 6; index++)
        {
            AddTextRow(pool, parent, $"Row {index}");
        }

        pool.CloseAllChildren(parent);

        Assert.IsEmpty(parent.ChildElements);
    }

    [TestMethod]
    public void CloseAllChildren_PoolsEachChildAndGrandchildExactlyOnce()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 400));
        var closedElements = new List<Element>();
        for (var index = 0; index < 3; index++)
        {
            var child = CreateContainer(pool, parent, new Vector2(200, 60));
            closedElements.Add(child);
            closedElements.Add(AddTextRow(pool, child, $"Row {index}"));
        }

        pool.CloseAllChildren(parent);

        Assert.IsTrue(closedElements.TrueForAll(element => element.PoolReturnCount == 1));
        var rentedWindows = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < 3; index++)
        {
            Assert.IsTrue(rentedWindows.Add(pool.CreateElement<Window>(null, new ElementOptions())), "A window pooled twice is handed out twice.");
        }
    }

    [TestMethod]
    public void CloseAllChildren_AClosedChildKeepsNoGrandchild()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 400));
        var child = CreateContainer(pool, parent, new Vector2(200, 60));
        AddTextRow(pool, child, "Row");

        pool.CloseAllChildren(parent);

        Assert.IsEmpty(child.ChildElements);
    }

    [TestMethod]
    public void CloseAllChildren_WrapContentParent_ShrinksAroundNothing()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, Vector2.Zero, ElementDisplayMode.WrapContent);
        parent.ContentPadding = Vector2.Zero;
        for (var index = 0; index < 4; index++)
        {
            AddTextRow(pool, parent, $"Row {index}");
        }

        Assert.AreEqual(RowHeight * 4, parent.CurrentSize.Y);

        pool.CloseAllChildren(parent);

        Assert.AreEqual(0f, parent.CurrentSize.Y);
    }

    [TestMethod]
    public void CloseAllChildren_WrapContentParent_ResizesOnce()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, Vector2.Zero, ElementDisplayMode.WrapContent);
        for (var index = 0; index < 4; index++)
        {
            AddTextRow(pool, parent, $"Row {index}");
        }

        var resizeCount = 0;
        parent.Resized += _ => resizeCount++;

        pool.CloseAllChildren(parent);

        Assert.AreEqual(1, resizeCount);
    }

    [TestMethod]
    public void CloseAllChildren_ScrolledParent_ScrollsBackToTheTop()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 60), scrollsVertically: true);
        for (var index = 0; index < 10; index++)
        {
            AddTextRow(pool, parent, $"Row {index}", width: 100f);
        }

        parent.ScrollBy(new Vector2(0, 80));

        pool.CloseAllChildren(parent);

        Assert.AreEqual(Vector2.Zero, parent.ScrollOffset);
        Assert.AreEqual(Vector2.Zero, parent.MaxScrollOffset);
    }

    [TestMethod]
    public void CloseAllChildren_AChildAddedAfterwards_IsTheOnlyChild()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 400));
        AddTextRow(pool, parent, "First");
        AddTextRow(pool, parent, "Second");

        pool.CloseAllChildren(parent);
        var replacement = AddTextRow(pool, parent, "Replacement");

        Assert.HasCount(1, parent.ChildElements);
        Assert.AreSame(replacement, parent.ChildElements[0]);
        Assert.AreEqual(parent.ContentAbsolutePosition, replacement.AbsolutePosition);
    }

    [TestMethod]
    public void CloseElement_AParentWithChildren_LeavesItWithNoChildAndItsOwnParentWithoutIt()
    {
        var pool = CreatePool();
        var root = CreateContainer(pool, null, new Vector2(200, 400));
        var parent = CreateContainer(pool, root, new Vector2(200, 60));
        var sibling = CreateContainer(pool, root, new Vector2(200, 60));
        AddTextRow(pool, parent, "Row");

        pool.CloseElement(parent);

        Assert.IsEmpty(parent.ChildElements);
        Assert.HasCount(1, root.ChildElements);
        Assert.AreEqual(root.ContentAbsolutePosition, sibling.AbsolutePosition, "The sibling after the closed element closes the gap.");
    }

    [TestMethod]
    public void SetRelativePosition_ARootMoved_MovesItsDescendantsWithoutResizingAny()
    {
        var pool = CreatePool();
        var root = CreateContainer(pool, null, new Vector2(200, 400));
        var row = AddTextRow(pool, root, "Row");
        var resizeCount = 0;
        root.Resized += _ => resizeCount++;
        row.Resized += _ => resizeCount++;

        root.SetRelativePosition(new Vector2(30, 40));

        Assert.AreEqual(new Vector2(30, 40), root.AbsolutePosition);
        Assert.AreEqual(root.ContentAbsolutePosition, row.AbsolutePosition);
        Assert.AreEqual(0, resizeCount);
    }

    [TestMethod]
    public void SetRelativePosition_ARootMoved_KeepsTheWrappedTextItHad()
    {
        var pool = CreatePool();
        var root = CreateContainer(pool, null, new Vector2(120, 400));
        var row = AddTextRow(pool, root, "A line of text long enough to wrap onto more than one line", width: 120f);
        var wrappedText = row.DisplayText.FormattedText;

        root.SetRelativePosition(new Vector2(30, 40));
        root.SetSize(new Vector2(120, 400));

        Assert.AreSame(wrappedText, row.DisplayText.FormattedText);
    }

    [TestMethod]
    public void SetRelativePosition_ThePositionItHas_RaisesNothing()
    {
        var pool = CreatePool();
        var root = CreateContainer(pool, null, new Vector2(200, 400));
        root.SetRelativePosition(new Vector2(30, 40));
        var eventCount = 0;
        root.Moved += _ => eventCount++;
        root.Resized += _ => eventCount++;

        root.SetRelativePosition(new Vector2(30, 40));

        Assert.AreEqual(0, eventCount);
    }

    [TestMethod]
    public void SetRelativePosition_AChildMovedTowardItsParentsEdge_IsStillClampedToTheSpaceLeft()
    {
        var pool = CreatePool();
        var root = CreateContainer(pool, null, new Vector2(200, 400), tileMode: ChildElementTileMode.Floating);
        root.ContentPadding = Vector2.Zero;
        root.SetSize(new Vector2(200, 400));
        var child = CreateContainer(pool, root, new Vector2(100, 100));

        child.SetRelativePosition(new Vector2(150, 0));

        Assert.AreEqual(50f, child.CurrentSize.X);
    }

    [TestMethod]
    public void SetRelativePosition_AChildMovedInAScrollingParent_GrowsTheScrollRange()
    {
        var pool = CreatePool();
        var root = CreateContainer(pool, null, new Vector2(200, 100), tileMode: ChildElementTileMode.Floating, scrollsVertically: true);
        var child = CreateContainer(pool, root, new Vector2(100, 50));

        child.SetRelativePosition(new Vector2(0, 300));

        Assert.AreEqual(350f - root.ContentSize.Y, root.MaxScrollOffset.Y);
    }

    [TestMethod]
    public void ScrollBy_PastTheEnd_MovesNoChildAgain()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 60), scrollsVertically: true);
        TextWindow lastRow = null!;
        for (var index = 0; index < 10; index++)
        {
            lastRow = AddTextRow(pool, parent, $"Row {index}", width: 100f);
        }

        parent.ScrollBy(new Vector2(0, 10_000));
        var moveCount = 0;
        lastRow.Moved += _ => moveCount++;

        parent.ScrollBy(new Vector2(0, 10_000));

        Assert.AreEqual(parent.MaxScrollOffset, parent.ScrollOffset);
        Assert.AreEqual(0, moveCount);
    }

    [TestMethod]
    public void AddingRowsToAScrollingParent_KeepsTheThumbInStepWithTheRange()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 60), scrollsVertically: true);
        for (var index = 0; index < 4; index++)
        {
            AddTextRow(pool, parent, $"Row {index}", width: 100f);
        }

        var thumbHeightWithFourRows = parent.VerticalScrollbarThumbRectangle.Height;

        for (var index = 4; index < 12; index++)
        {
            AddTextRow(pool, parent, $"Row {index}", width: 100f);
        }

        Assert.IsLessThan(thumbHeightWithFourRows, parent.VerticalScrollbarThumbRectangle.Height);
    }

    [TestMethod]
    public void DrawsWithin_ARowScrolledOutOfItsParentsContent_IsFalse()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 60), scrollsVertically: true);
        var rows = new List<TextWindow>();
        for (var index = 0; index < 10; index++)
        {
            rows.Add(AddTextRow(pool, parent, $"Row {index}", width: 100f));
        }

        Assert.IsTrue(rows[0].DrawsWithin(parent.ContentRectangle));
        Assert.IsFalse(rows[9].DrawsWithin(parent.ContentRectangle));

        parent.ScrollBy(new Vector2(0, 10_000));

        Assert.IsFalse(rows[0].DrawsWithin(parent.ContentRectangle));
        Assert.IsTrue(rows[9].DrawsWithin(parent.ContentRectangle));
    }

    [TestMethod]
    public void DrawsWithin_ARowJustOutsideByLessThanAGlowReaches_IsTrue()
    {
        var pool = CreatePool();
        var parent = CreateContainer(pool, null, new Vector2(200, 60), scrollsVertically: true);
        var rows = new List<TextWindow>();
        for (var index = 0; index < 4; index++)
        {
            rows.Add(AddTextRow(pool, parent, $"Row {index}", width: 100f));
        }

        var gapBelowContent = rows[3].Rectangle.Top - parent.ContentRectangle.Bottom;
        parent.ScrollBy(new Vector2(0, gapBelowContent - 3));

        Assert.AreEqual(parent.ContentRectangle.Bottom + 3, rows[3].Rectangle.Top);
        Assert.IsTrue(rows[3].DrawsWithin(parent.ContentRectangle));
    }

    [TestMethod]
    public void GetInkCenterAtOrigin_MoreStringsThanAreKept_ForgetsThemAndStillAnswersTheSame()
    {
        var measurer = new FontStashTextMeasurer(TestFonts.Shared.GetFont(16));
        var firstAnswer = measurer.GetInkCenterAtOrigin("0");

        for (var number = 1; number <= FontStashTextMeasurer.MaximumKeptInkCenters + 10; number++)
        {
            measurer.GetInkCenterAtOrigin(number.ToString());
        }

        Assert.IsLessThanOrEqualTo(FontStashTextMeasurer.MaximumKeptInkCenters, measurer.KeptInkCenterCount);
        Assert.AreEqual(firstAnswer, measurer.GetInkCenterAtOrigin("0"));
    }

    [TestMethod]
    public void GetInkCenterAtOrigin_AnEqualStringOfAnotherInstance_AnswersTheSame()
    {
        var measurer = new FontStashTextMeasurer(TestFonts.Shared.GetFont(16));
        var firstInstance = new string(['A', 'b']);
        var secondInstance = new string(['A', 'b']);

        Assert.AreEqual(measurer.GetInkCenterAtOrigin(firstInstance), measurer.GetInkCenterAtOrigin(secondInstance));
        Assert.AreEqual(1, measurer.KeptInkCenterCount);
    }
}
