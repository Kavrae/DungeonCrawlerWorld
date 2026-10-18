using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class DropdownTests
{
    private static readonly string[] Colours = ["Red", "Green", "Blue"];

    private static DropdownField AddField(ControlTestHarness harness, Vector2 position, IReadOnlyList<string> labels, int selectedIndex = 0) =>
        harness.AddRoot<DropdownField>(position, new Vector2(180, 24), field => field.SetOptions(labels, selectedIndex));

    private static Button Row(ControlTestHarness harness, int optionIndex) => harness.Dropdowns.Menu.ChildElements.OfType<Button>().ElementAt(optionIndex);

    [TestMethod]
    public void ClickingAField_OpensItsListUnderIt_AtLeastAsWide_WithTheChosenOptionMarked()
    {
        var harness = new ControlTestHarness();
        var field = AddField(harness, new Vector2(100, 100), Colours, selectedIndex: 1);

        harness.Click(field);

        Assert.IsTrue(field.IsListOpen);
        var menu = harness.Dropdowns.Menu;
        Assert.AreEqual(field.Rectangle.Bottom, menu.Rectangle.Top);
        Assert.AreEqual(field.Rectangle.Left, menu.Rectangle.Left);
        Assert.IsGreaterThanOrEqualTo(field.Rectangle.Width, menu.Rectangle.Width);
        Assert.IsTrue(Row(harness, 1).IsMarked);
        Assert.IsFalse(Row(harness, 0).IsMarked);
        Assert.AreEqual(1, menu.HighlightedIndex);
    }

    [TestMethod]
    public void ClickingAnOption_ChoosesIt_RaisesSelectionChanged_AndCloses()
    {
        var harness = new ControlTestHarness();
        var field = AddField(harness, new Vector2(100, 100), Colours);
        var chosen = new List<int>();
        field.SelectionChanged += chosen.Add;

        harness.Click(field);
        harness.Click(Row(harness, 2));

        CollectionAssert.AreEqual(new[] { 2 }, chosen);
        Assert.AreEqual("Blue", field.SelectedLabel);
        Assert.IsFalse(harness.Dropdowns.IsOpen);
    }

    [TestMethod]
    public void ClickingTheFieldAgain_ClosesItsList_RatherThanReopeningIt()
    {
        var harness = new ControlTestHarness();
        var field = AddField(harness, new Vector2(100, 100), Colours);

        harness.Click(field);
        harness.Click(field);

        Assert.IsFalse(harness.Dropdowns.IsOpen);
    }

    [TestMethod]
    public void ChoosingTheOptionAlreadyChosen_RaisesNothing()
    {
        var harness = new ControlTestHarness();
        var field = AddField(harness, new Vector2(100, 100), Colours, selectedIndex: 0);
        var raised = 0;
        field.SelectionChanged += _ => raised++;

        harness.Click(field);
        harness.Click(Row(harness, 0));

        Assert.AreEqual(0, raised);
    }

    [TestMethod]
    public void Keyboard_DownOpensTheFocusedField_UpDownMoveTheHighlight_EnterChooses()
    {
        var harness = new ControlTestHarness();
        var field = AddField(harness, new Vector2(100, 100), Colours, selectedIndex: 0);
        harness.Click(field);
        harness.Click(field);
        Assert.IsTrue(field.IsFocused);

        harness.Press(Keys.Down);
        Assert.IsTrue(field.IsListOpen, "Down on the focused field opens its list.");
        Assert.AreEqual(0, harness.Dropdowns.Menu.HighlightedIndex);

        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        Assert.AreEqual(0, harness.Dropdowns.Menu.HighlightedIndex, "The highlight wraps past the last option.");

        harness.Press(Keys.Up);
        Assert.AreEqual(2, harness.Dropdowns.Menu.HighlightedIndex);

        harness.Press(Keys.Enter);
        Assert.AreEqual(2, field.SelectedIndex);
        Assert.IsFalse(harness.Dropdowns.IsOpen);
    }

    [TestMethod]
    public void TheHighlight_SkipsHeadersAndDisabledOptions()
    {
        var harness = new ControlTestHarness();
        harness.Dropdowns.OpenAt(new Vector2(50, 50),
        [
            DropdownOption.Header("Group"),
            new DropdownOption("First", null, Enabled: true, static () => { }),
            new DropdownOption("Off", null, Enabled: false, static () => { }),
            new DropdownOption("Last", null, Enabled: true, static () => { }),
        ]);

        harness.Press(Keys.Down);
        Assert.AreEqual(1, harness.Dropdowns.Menu.HighlightedIndex);
        harness.Press(Keys.Down);
        Assert.AreEqual(3, harness.Dropdowns.Menu.HighlightedIndex);
    }

    [TestMethod]
    public void EscapeAndAnOutsidePress_CloseTheList()
    {
        var harness = new ControlTestHarness();
        var field = AddField(harness, new Vector2(100, 100), Colours);

        harness.Click(field);
        harness.Press(Keys.Escape);
        Assert.IsFalse(harness.Dropdowns.IsOpen);

        harness.Click(field);
        harness.Click(new Point(900, 900));
        Assert.IsFalse(harness.Dropdowns.IsOpen);
    }

    [TestMethod]
    public void ALongList_ScrollsPastTheMaximumHeight_AndTheWheelScrollsIt()
    {
        var harness = new ControlTestHarness();
        var labels = Enumerable.Range(0, 60).Select(index => $"Option {index}").ToArray();
        var field = AddField(harness, new Vector2(100, 100), labels);

        harness.Click(field);

        var menu = harness.Dropdowns.Menu;
        Assert.IsLessThanOrEqualTo(DropdownField.ListMaximumHeight + 20, menu.Rectangle.Height);
        Assert.IsTrue(menu.CanUserScrollVertical);

        harness.Wheel(menu.Rectangle.Center, notches: -2);
        Assert.IsGreaterThan(0f, menu.ScrollOffset.Y);
    }

    [TestMethod]
    public void KeyboardMovingPastTheVisibleRows_ScrollsTheHighlightIntoView()
    {
        var harness = new ControlTestHarness();
        var labels = Enumerable.Range(0, 60).Select(index => $"Option {index}").ToArray();
        var field = AddField(harness, new Vector2(100, 100), labels);
        harness.Click(field);

        harness.Press(Keys.Up);

        Assert.AreEqual(59, harness.Dropdowns.Menu.HighlightedIndex);
        Assert.AreEqual(harness.Dropdowns.Menu.MaxScrollOffset.Y, harness.Dropdowns.Menu.ScrollOffset.Y);
    }

    [TestMethod]
    public void AFieldNearTheBottom_OpensItsListAboveIt()
    {
        var harness = new ControlTestHarness();
        var field = AddField(harness, new Vector2(100, ControlTestHarness.ScreenSize.Y - 40), Colours);

        harness.Click(field);

        Assert.AreEqual(field.Rectangle.Top, harness.Dropdowns.Menu.Rectangle.Bottom);
    }

    [TestMethod]
    public void AContextMenu_StillOpensAtThePoint_WithNothingMarked()
    {
        var harness = new ControlTestHarness();
        var contextMenus = new ContextMenuController(harness.Dropdowns);

        contextMenus.Open(new Vector2(300, 200), [new DropdownOption("Copy", "Ctrl+C", Enabled: true, static () => { })]);

        Assert.AreEqual(new Point(300, 200), harness.Dropdowns.Menu.Rectangle.Location);
        Assert.IsNull(harness.Dropdowns.Owner);
        Assert.IsFalse(Row(harness, 0).IsMarked);
        Assert.AreEqual(-1, harness.Dropdowns.Menu.HighlightedIndex);
    }
}
