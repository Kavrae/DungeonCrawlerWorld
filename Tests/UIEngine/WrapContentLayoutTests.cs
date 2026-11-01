using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class WrapContentLayoutTests
{
    private static Window CreateWrapContentRoot(ElementPoolService pool, string? titleText = null)
    {
        var root = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(200, 250), MaximumSize = new Vector2(800, 600), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = titleText is not null, TitleText = titleText ?? string.Empty },
        });
        root.Initialize();
        return root;
    }

    private static Window AddChild(ElementPoolService pool, Window parent, Vector2 size, ElementDisplayMode displayMode = ElementDisplayMode.Fixed)
    {
        var child = pool.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = size, DisplayMode = displayMode },
        });
        parent.AddChild(child);
        return child;
    }

    [TestMethod]
    public void AWrapContentPopupHoldingAMultilineTextBox_KeepsTheBoxsWidth_AndGrowsItOneLineAtATimeUpToItsCap()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var popup = CreateWrapContentRoot(pool, "New Quest");
        var textBox = pool.CreateElement<TextBox>(popup, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 190), MaximumSize = new Vector2(400, 190), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Multiline = true },
        });
        popup.AddChild(textBox);
        var lineHeight = textBox.ContentFont.LineHeight;
        var twoLineHeight = lineHeight * 2 + 3 * 2;

        Assert.AreEqual(new Vector2(400, twoLineHeight), textBox.CurrentSize);
        Assert.IsGreaterThanOrEqualTo(400f, popup.ContentSize.X);

        var resizeCount = 0;
        textBox.Resized += _ => resizeCount++;
        var popupHeightBefore = popup.CurrentSize.Y;
        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());
        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());

        Assert.AreEqual(twoLineHeight + lineHeight, textBox.CurrentSize.Y);
        Assert.AreEqual(1, resizeCount, "One resize for the one line that needed room, not a clamped size and then a corrected one.");
        Assert.AreEqual(popupHeightBefore + lineHeight, popup.CurrentSize.Y);

        for (var index = 0; index < 30; index++)
        {
            textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());
        }

        Assert.AreEqual(new Vector2(400, 190), textBox.CurrentSize);
    }

    [TestMethod]
    public void AChildResizingItself_RefitsEveryWrapContentAncestor()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var outer = CreateWrapContentRoot(pool);
        var inner = AddChild(pool, outer, Vector2.Zero, ElementDisplayMode.WrapContent);
        var child = AddChild(pool, inner, new Vector2(50, 40));

        child.SetSize(new Vector2(300, 200));

        Assert.AreEqual(new Vector2(300, 200), child.CurrentSize);
        Assert.IsGreaterThanOrEqualTo(300f, inner.ContentSize.X);
        Assert.IsGreaterThanOrEqualTo(200f, inner.ContentSize.Y);
        Assert.IsGreaterThanOrEqualTo(inner.CurrentSize.X, outer.ContentSize.X);
        Assert.IsGreaterThanOrEqualTo(inner.CurrentSize.Y, outer.ContentSize.Y);
    }

    [TestMethod]
    public void AWrapContentElement_StopsAtItsCap_AndOffersItsChildrenOnlyTheContentAreaInsideIt()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(200, 100), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, TitleText = "Capped" },
        });
        parent.Initialize();
        var child = AddChild(pool, parent, new Vector2(500, 500));

        Assert.AreEqual(new Vector2(200, 100), parent.CurrentSize);
        Assert.AreEqual(parent.ContentSize, child.CurrentSize);
        Assert.AreEqual(parent.CurrentSize - parent.ContentInset, parent.ContentSize);
    }

    [TestMethod]
    public void AWrapContentElementsContentSize_IsTheAreaItsChildrenOccupy_WithItsPaddingOutsideIt()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = CreateWrapContentRoot(pool);
        AddChild(pool, parent, new Vector2(50, 40));

        Assert.AreEqual(new Vector2(50, 40), parent.ContentSize);
        Assert.AreEqual(parent.ContentSize + parent.ContentInset, parent.CurrentSize);
    }

    [TestMethod]
    public void AScrollingWrapContentElement_PastItsCap_ScrollsTheRest()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(300, 150), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserScrollVertical = true },
        });
        parent.Initialize();
        AddChild(pool, parent, new Vector2(100, 400));

        Assert.AreEqual(150f, parent.CurrentSize.Y);
        Assert.AreEqual(400f - parent.ContentSize.Y, parent.MaxScrollOffset.Y);
    }

    [TestMethod]
    public void ATiledChildWhoseTextChangesItsHeight_MovesTheSiblingsAfterItAtOnce()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var column = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { Size = new Vector2(300, 200), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true },
        });
        column.Initialize();
        TextWindow AddRow(string text)
        {
            var row = pool.CreateElement<TextWindow>(column, new ElementOptions
            {
                Layout = new ElementLayoutOptions { MaximumSize = new Vector2(300, 10000), DisplayMode = ElementDisplayMode.WrapContent },
                Text = new TextOptions { Text = text },
            });
            column.AddChild(row);
            return row;
        }

        var first = AddRow("one\ntwo\nthree\nfour");
        var second = AddRow("after");

        first.UpdateText("one");
        Assert.AreEqual(first.RelativePosition.Y + first.CurrentSize.Y, second.RelativePosition.Y, "Shrunk: no gap left behind.");

        first.UpdateText("one\ntwo\nthree\nfour\nfive\nsix");
        Assert.AreEqual(first.RelativePosition.Y + first.CurrentSize.Y, second.RelativePosition.Y, "Grown: no overlap.");

        var fixedRow = pool.CreateElement<Window>(column, new ElementOptions { Layout = new ElementLayoutOptions { Size = new Vector2(100, 20), DisplayMode = ElementDisplayMode.Fixed } });
        column.AddChild(fixedRow, insertIndex: 0);
        fixedRow.SetSize(new Vector2(100, 60));
        Assert.AreEqual(60f, first.RelativePosition.Y, "A child resized with SetSize moves its siblings too.");
    }

    [TestMethod]
    public void HidingATiledChild_ClosesItsGap_AndShowingItAgain_MeasuresItAndOpensItBack()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var column = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { Size = new Vector2(300, 300), DisplayMode = ElementDisplayMode.Fixed },
        });
        column.Initialize();
        var first = AddChild(pool, column, new Vector2(100, 30));
        var second = AddChild(pool, column, new Vector2(100, 40));

        first.IsVisible = false;
        Assert.AreEqual(0f, second.RelativePosition.Y);

        first.IsVisible = true;
        Assert.AreEqual(30f, second.RelativePosition.Y);
    }

    [TestMethod]
    public void MovingAChildInsideAScrollingParent_UpdatesHowFarItScrolls()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true, ScrollbarVisibility = ScrollbarVisibility.Hidden },
        });
        parent.Initialize();
        var child = AddChild(pool, parent, new Vector2(50, 50));

        child.SetRelativePosition(new Vector2(0, 300));

        Assert.AreEqual(350f - parent.ContentSize.Y, parent.MaxScrollOffset.Y);
    }

    [TestMethod]
    public void ATiledWrapContentElement_FitsAroundChildrenThatChangedSizeInItsOwnMeasure()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var column = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(600, 2000), DisplayMode = ElementDisplayMode.WrapContent },
        });
        column.Initialize();
        var longText = string.Join(" ", Enumerable.Repeat("words that wrap", 20));
        TextWindow AddRow()
        {
            var row = pool.CreateElement<TextWindow>(column, new ElementOptions
            {
                Layout = new ElementLayoutOptions { DisplayMode = ElementDisplayMode.WrapContent },
                Text = new TextOptions { Text = longText },
            });
            column.AddChild(row);
            return row;
        }

        var first = AddRow();
        var second = AddRow();
        var firstHeightBefore = first.CurrentSize.Y;

        column.SetMaximumSize(new Vector2(150, 2000));
        column.SetSize(Vector2.Zero);

        Assert.IsGreaterThan(firstHeightBefore, first.CurrentSize.Y, "Narrower, so the rows wrap taller.");
        Assert.AreEqual(first.CurrentSize.Y, second.RelativePosition.Y);
        Assert.AreEqual(first.CurrentSize.Y + second.CurrentSize.Y, column.ContentSize.Y);
    }

    [TestMethod]
    public void ChildrenResizingInsideALayoutBatch_RefitTheirWrapContentParentOnce_WhenTheBatchEnds()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var parent = CreateWrapContentRoot(pool);
        var children = Enumerable.Range(0, 5).Select(_ => AddChild(pool, parent, new Vector2(20, 20))).ToList();
        var parentResizeCount = 0;
        parent.Resized += _ => parentResizeCount++;

        using (parent.BeginLayoutBatch())
        {
            for (var index = 0; index < children.Count; index++)
            {
                children[index].SetSize(new Vector2(40 + index * 10, 20));
                Assert.AreEqual(new Vector2(40 + index * 10, 20), children[index].CurrentSize, "A child is measured at once; only the parent's re-fit waits.");
            }

            Assert.AreEqual(0, parentResizeCount);
        }

        Assert.AreEqual(1, parentResizeCount);
        Assert.IsGreaterThanOrEqualTo(80f, parent.ContentSize.X);
    }
}
