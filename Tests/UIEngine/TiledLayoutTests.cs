using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>A tiled parent's gap, cross-axis alignment and the child that takes the space left, laid out by the ordinary layout pass.</summary>
[TestClass]
[DoNotParallelize]
public sealed class TiledLayoutTests
{
    private const float Gap = 4f;

    private static ElementPoolService CreatePool() => TestElementPoolServiceFactory.Create(TestFonts.Shared);

    private static Window CreateRow(ElementPoolService pool, Vector2 size, ChildElementTileMode tileMode, ChildCrossAlignment alignment)
    {
        var row = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = tileMode, ChildGap = Gap, ChildCrossAlignment = alignment },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
        });
        row.ContentPadding = Vector2.Zero;
        row.Initialize();
        return row;
    }

    private static Window AddBox(ElementPoolService pool, Window parent, Vector2 size)
    {
        var box = pool.CreateElement<Window>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = size, DisplayMode = ElementDisplayMode.Fixed },
        });
        parent.AddChild(box);
        return box;
    }

    private static Label AddLabel(ElementPoolService pool, Window parent, string text, bool fillsRemaining = false)
    {
        var label = pool.CreateElement<Label>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { FillsRemaining = fillsRemaining },
            Text = new TextOptions { Text = text },
        });
        parent.AddChild(label);
        return label;
    }

    private static float RightEdge(Element element) => element.RelativePosition.X + element.CurrentSize.X;

    [TestMethod]
    public void AHorizontalRow_PlacesEachChildAfterTheGap_CentresMixedHeights_AndGivesTheFillerWhatIsLeft()
    {
        var pool = CreatePool();
        var row = CreateRow(pool, new Vector2(300, 40), ChildElementTileMode.Horizontal, ChildCrossAlignment.Center);

        var icon = AddBox(pool, row, new Vector2(20, 20));
        var name = AddLabel(pool, row, "Name", fillsRemaining: true);
        var badge = AddBox(pool, row, new Vector2(30, 10));
        var count = AddLabel(pool, row, "99");

        Assert.AreEqual(new Vector2(0, 10), icon.RelativePosition);
        Assert.AreEqual(20 + Gap, name.RelativePosition.X);
        Assert.AreEqual(300 - 20 - 30 - count.CurrentSize.X - 3 * Gap, name.CurrentSize.X);
        Assert.AreEqual(MathF.Floor((40 - name.CurrentSize.Y) / 2), name.RelativePosition.Y);
        Assert.AreEqual(RightEdge(name) + Gap, badge.RelativePosition.X);
        Assert.AreEqual(15f, badge.RelativePosition.Y);
        Assert.AreEqual(300f, RightEdge(count), "The filler pushes the last child to the row's right end.");
        Assert.IsFalse(name.IsCut);
    }

    [TestMethod]
    public void ATiledChildsTextChanging_ReLaysOutTheRow_SoTheFillerShrinksAndTheLastChildStaysAtTheEnd()
    {
        var pool = CreatePool();
        var row = CreateRow(pool, new Vector2(300, 40), ChildElementTileMode.Horizontal, ChildCrossAlignment.Center);
        AddBox(pool, row, new Vector2(20, 20));
        var name = AddLabel(pool, row, "Name", fillsRemaining: true);
        var badge = AddBox(pool, row, new Vector2(30, 10));
        var count = AddLabel(pool, row, "9");
        var fillerWidthBefore = name.CurrentSize.X;
        var countWidthBefore = count.CurrentSize.X;

        count.SetText("123456789");

        var growth = count.CurrentSize.X - countWidthBefore;
        Assert.IsGreaterThan(0f, growth);
        Assert.AreEqual(fillerWidthBefore - growth, name.CurrentSize.X);
        Assert.AreEqual(RightEdge(name) + Gap, badge.RelativePosition.X);
        Assert.AreEqual(RightEdge(badge) + Gap, count.RelativePosition.X);
        Assert.AreEqual(300f, RightEdge(count));
    }

    [TestMethod]
    public void AHiddenChild_GivesItsSpaceAndOneGapToTheFiller()
    {
        var pool = CreatePool();
        var row = CreateRow(pool, new Vector2(300, 40), ChildElementTileMode.Horizontal, ChildCrossAlignment.Start);
        AddBox(pool, row, new Vector2(20, 20));
        var name = AddLabel(pool, row, "Name", fillsRemaining: true);
        var badge = AddBox(pool, row, new Vector2(30, 10));
        var fillerWidthBefore = name.CurrentSize.X;

        badge.IsVisible = false;

        Assert.AreEqual(fillerWidthBefore + 30 + Gap, name.CurrentSize.X);
        Assert.AreEqual(0f, name.RelativePosition.Y);
    }

    [TestMethod]
    public void AVerticalColumn_GapsItsChildren_AndAlignsThemToTheEnd()
    {
        var pool = CreatePool();
        var column = CreateRow(pool, new Vector2(100, 200), ChildElementTileMode.Vertical, ChildCrossAlignment.End);
        var first = AddBox(pool, column, new Vector2(40, 30));
        var filler = pool.CreateElement<Window>(column, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(60, 10), DisplayMode = ElementDisplayMode.Fixed, FillsRemaining = true },
        });
        column.AddChild(filler);
        var last = AddBox(pool, column, new Vector2(100, 50));

        Assert.AreEqual(new Vector2(60, 0), first.RelativePosition);
        Assert.AreEqual(new Vector2(40, 30 + Gap), filler.RelativePosition);
        Assert.AreEqual(new Vector2(60, 200 - 30 - 50 - 2 * Gap), filler.CurrentSize);
        Assert.AreEqual(new Vector2(0, 150), last.RelativePosition);
    }

    [TestMethod]
    public void AFillerInAWrapContentParent_KeepsItsOwnSize()
    {
        var pool = CreatePool();
        var row = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal, ChildGap = Gap },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(800, 600), DisplayMode = ElementDisplayMode.WrapContent },
        });
        row.ContentPadding = Vector2.Zero;
        row.Initialize();
        AddBox(pool, row, new Vector2(20, 20));
        var name = AddLabel(pool, row, "Name", fillsRemaining: true);
        var naturalWidth = TestFonts.Shared.GetTextMeasurer(name.Font).MeasureWidth("Name");

        Assert.AreEqual(MathF.Ceiling(naturalWidth), name.CurrentSize.X);
        Assert.AreEqual(20 + Gap + name.CurrentSize.X, row.ContentSize.X);
    }

    [TestMethod]
    public void AContentIndent_StartsTheFirstTiledChildPastIt_AndAWrapContentRowGrowsByIt()
    {
        const float Indent = 12f;
        var pool = CreatePool();
        var row = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal, ChildGap = Gap, ContentIndent = Indent },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(800, 600), DisplayMode = ElementDisplayMode.WrapContent },
        });
        row.ContentPadding = Vector2.Zero;
        row.Initialize();
        var first = AddBox(pool, row, new Vector2(20, 20));
        var second = AddBox(pool, row, new Vector2(30, 20));

        Assert.AreEqual(Indent, first.RelativePosition.X);
        Assert.AreEqual(Indent + 20 + Gap, second.RelativePosition.X);
        Assert.AreEqual(Indent + 20 + Gap + 30, row.ContentSize.X);
    }
}
