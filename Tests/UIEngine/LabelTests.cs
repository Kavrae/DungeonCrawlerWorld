using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>Label: one line as wide as its text within what it is offered, cut with "..." past that, and measured once per change.</summary>
[TestClass]
[DoNotParallelize]
public sealed class LabelTests
{
    private const string LongText = "A name far too long to fit in the narrow row it was given";

    private static (ElementPoolService Pool, Window Row) CreateRow(float width)
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var row = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(50, 50), Size = new Vector2(width, 30), DisplayMode = ElementDisplayMode.Fixed },
        });
        row.ContentPadding = Vector2.Zero;
        row.Initialize();
        return (pool, row);
    }

    private static Label AddLabel(ElementPoolService pool, Window row, string text)
    {
        var label = pool.CreateElement<Label>(row, new ElementOptions { Text = new TextOptions { Text = text } });
        row.AddChild(label);
        return label;
    }

    [TestMethod]
    public void ALabelThatFits_IsAsWideAsItsText_OneLineTall_AndUncut()
    {
        var (pool, row) = CreateRow(400);
        var label = AddLabel(pool, row, "Short");

        var textWidth = TestFonts.Shared.GetTextMeasurer(label.Font).MeasureWidth("Short");
        Assert.AreEqual(new Vector2(MathF.Ceiling(textWidth), MathF.Ceiling(label.Font.LineHeight)), label.CurrentSize);
        Assert.AreSame(label.Text, label.ShownText);
        Assert.IsFalse(label.IsCut);
    }

    [TestMethod]
    public void ALabelWiderThanItsRow_IsCutToTheRowWithAnEllipsis()
    {
        var (pool, row) = CreateRow(80);
        var label = AddLabel(pool, row, LongText);

        Assert.AreEqual(80f, label.CurrentSize.X);
        Assert.IsTrue(label.IsCut);
        Assert.EndsWith("...", label.ShownText);
        Assert.IsLessThanOrEqualTo(80f, TestFonts.Shared.GetTextMeasurer(label.Font).MeasureWidth(label.ShownText));
    }

    [TestMethod]
    public void ALabelIsMeasuredOncePerChange_NotPerLayoutPassOrRead()
    {
        var (pool, row) = CreateRow(80);
        var label = AddLabel(pool, row, LongText);
        var shownBefore = label.ShownText;

        row.MeasureAndArrange();
        label.SetText(LongText);
        row.MeasureAndArrange();

        Assert.AreSame(shownBefore, label.ShownText, "Nothing changed, so the cut kept is the one made before.");

        label.SetText("Other");

        Assert.AreEqual("Other", label.ShownText);
        Assert.IsFalse(label.IsCut);
        Assert.IsLessThan(80f, label.CurrentSize.X, "A new text re-lays out the label at its own width.");
    }
}
