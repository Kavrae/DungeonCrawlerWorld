using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.Fields;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class TooltipIndicatorTests
{
    [TestMethod]
    public void AnElement_ShowsNoIndicator_UnlessItOptsIn()
    {
        var harness = new ControlTestHarness();

        var button = harness.AddRoot<Button>(new Vector2(100, 100), new Vector2(80, 24), configure: button => button.TooltipText = "Inventory");

        Assert.IsFalse(button.ShowsTooltipIndicator);
    }

    [TestMethod]
    public void AnElementReusedFromThePool_DropsTheIndicatorItsLastUseOptedInto()
    {
        var harness = new ControlTestHarness();
        var first = harness.Pool.CreateElement<Button>(null, new ElementOptions());
        first.ShowsTooltipIndicator = true;
        harness.Pool.CloseElement(first);

        var reused = harness.Pool.CreateElement<Button>(null, new ElementOptions());

        Assert.AreSame(first, reused);
        Assert.IsFalse(reused.ShowsTooltipIndicator);
    }

    [TestMethod]
    public void AMenuRow_ShowsTheIndicator_SoAnOptionWithATooltipIsMarked()
    {
        var harness = new ControlTestHarness();

        harness.Dropdowns.OpenAt(new Vector2(300, 200), [new DropdownOption("Paste", "Ctrl+V", Enabled: false, () => { }) { TooltipText = "Nothing to paste" }, new DropdownOption("Copy", null, Enabled: true, () => { })]);

        var rows = harness.Dropdowns.Menu.OptionRows;
        Assert.IsTrue(rows[0]!.ShowsTooltipIndicator);
        Assert.AreEqual("Nothing to paste", rows[0]!.TooltipText);
        Assert.IsNull(rows[1]!.TooltipText);
    }

    [TestMethod]
    public void AMenuRowsText_EndsWhereItsLabelsTextEnds_NotAtTheRowsEdge()
    {
        var harness = new ControlTestHarness();

        harness.Dropdowns.OpenAt(new Vector2(300, 200), [new DropdownOption("A much longer option", null, Enabled: true, () => { }), new DropdownOption("Cut", null, Enabled: true, () => { }) { TooltipText = "Removes it" }]);

        var row = harness.Dropdowns.Menu.OptionRows[1]!;
        var label = row.ChildElements.OfType<Label>().Single();
        var textEnd = row.TextEndPosition!.Value;
        Assert.AreEqual(label.TextEndPosition!.Value, textEnd);
        Assert.AreEqual(label.ContentAbsolutePosition.X + TestFonts.Shared.GetTextMeasurer(label.Font).MeasureWidth("Cut"), textEnd.X, 0.5f);
        Assert.IsLessThan(row.Rectangle.Right - 20, textEnd.X);
    }

    [TestMethod]
    public void AWrappedTextWindowsText_EndsAtItsLastLine()
    {
        var harness = new ControlTestHarness();
        var text = harness.AddRoot<TextWindow>(new Vector2(100, 100), new Vector2(120, 200), configure: window =>
        {
            window.OriginalText = "Grants a size 3 Light aura with strength 8 for 11s";
            window.ReformatDisplayText();
        });

        var textEnd = text.TextEndPosition!.Value;

        Assert.IsGreaterThan(1, text.DisplayText.LineCount);
        Assert.IsGreaterThan(text.ContentAbsolutePosition.Y + text.ContentFont.LineHeight, textEnd.Y);
        Assert.IsLessThan(text.Rectangle.Right, textEnd.X);
    }

    [TestMethod]
    public void AnElementWithNoText_HasNoTextEnd_SoItsIndicatorTakesTheCorner()
    {
        var harness = new ControlTestHarness();

        var window = harness.AddRoot<Window>(new Vector2(100, 100), new Vector2(80, 24));

        Assert.IsNull(window.TextEndPosition);
    }

    [TestMethod]
    public void AFieldRowAndASectionHeader_ShowTheIndicator()
    {
        var harness = new ControlTestHarness();
        harness.Pool.RegisterFactory(() => new FieldRow(TestElementServices.For(harness.Pool)));
        harness.Pool.RegisterFactory(() => new FieldSectionHeader(TestElementServices.For(harness.Pool)));

        var row = harness.Pool.CreateElement<FieldRow>(null, new ElementOptions());
        var header = harness.Pool.CreateElement<FieldSectionHeader>(null, new ElementOptions());
        header.Configure("Effects", depth: 0, isCollapsible: true, isExpanded: true, buttonLabels: [], hint: "What using it does");

        Assert.IsTrue(row.ShowsTooltipIndicator);
        Assert.IsTrue(header.ShowsTooltipIndicator);
        Assert.AreEqual("What using it does", header.TooltipText);
    }
}
