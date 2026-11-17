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

    private static PressableElement Row(ControlTestHarness harness, int optionIndex) => harness.Dropdowns.Menu.OptionRows[optionIndex]!;

    /// <summary>An icon's box and the gap after it, which every row of a menu holding one gives it.</summary>
    private const int IconAndGapWidth = 20;

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
        Assert.AreEqual(1, menu.MarkedIndex);
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
        Assert.AreEqual(-1, harness.Dropdowns.Menu.MarkedIndex);
        Assert.AreEqual(-1, harness.Dropdowns.Menu.HighlightedIndex);
    }

    [TestMethod]
    public void AnyRowWithAnIcon_GivesEveryRowItsPlace_WidensTheList_AndEachRowKeepsItsTooltip()
    {
        var harness = new ControlTestHarness();
        var contextMenus = new ContextMenuController(harness.Dropdowns);
        var star = new GlyphIcon("*", Color.Red);
        DropdownOption[] plain = [new("Copy", null, Enabled: true, static () => { }), new("Paste", null, Enabled: true, static () => { })];
        contextMenus.Open(new Vector2(300, 200), plain);
        var plainWidth = harness.Dropdowns.Menu.Rectangle.Width;
        Assert.IsNull(MenuRows.IconOf(Row(harness, 0)));

        contextMenus.Open(new Vector2(300, 200), [plain[0] with { Icon = star, TooltipText = "Copies it" }, plain[1]]);

        Assert.AreEqual(star, MenuRows.IconOf(Row(harness, 0)));
        Assert.AreEqual(GlyphIcon.Blank, MenuRows.IconOf(Row(harness, 1)));
        Assert.AreEqual("Copies it", Row(harness, 0).TooltipText);
        Assert.IsNull(Row(harness, 1).TooltipText);
        Assert.AreEqual(plainWidth + IconAndGapWidth, harness.Dropdowns.Menu.Rectangle.Width);
    }

    [TestMethod]
    public void EveryRow_IsAsWideAsTheWidest_ItsLabelTakesTheRest_AndItsHotkeySitsAtTheRightEnd()
    {
        var harness = new ControlTestHarness();
        var contextMenus = new ContextMenuController(harness.Dropdowns);

        contextMenus.Open(new Vector2(300, 200),
        [
            new DropdownOption("Cut", "Ctrl+X", Enabled: true, static () => { }),
            new DropdownOption("Select everything there is", null, Enabled: true, static () => { }),
            new DropdownOption("Paste", "Ctrl+V", Enabled: false, static () => { }),
        ]);

        var rows = harness.Dropdowns.Menu.OptionRows.Select(row => row!).ToList();
        Assert.IsTrue(rows.All(row => row is Button), "An option without a checkbox is a button.");
        Assert.AreEqual(1, rows.Select(row => row.Rectangle.Width).Distinct().Count());
        var cutHotkey = rows[0].ChildElements.OfType<Label>().Single(label => label.Text == "Ctrl+X");
        Assert.AreEqual(rows[0].Rectangle.Right - 4, cutHotkey.Rectangle.Right);
        var cutLabel = rows[0].ChildElements.OfType<Label>().Single(label => label.FillsRemaining);
        Assert.AreEqual(cutHotkey.Rectangle.Left - 24, cutLabel.Rectangle.Right, "The label takes what the hotkey and its gap leave.");
        Assert.IsFalse(rows[2].Enabled);
    }

    [TestMethod]
    public void AnOptionUnderAHeading_StartsIndented_AndEveryRowStaysAsWideAsTheWidest()
    {
        var harness = new ControlTestHarness();

        harness.Dropdowns.OpenAt(new Vector2(300, 200), indentsUnderHeadings: true, options:
        [
            new DropdownOption("Before any heading", null, Enabled: true, static () => { }),
            DropdownOption.Header("Group"),
            new DropdownOption("Under it", null, Enabled: true, static () => { }),
        ]);

        var before = Row(harness, 0);
        var under = Row(harness, 2);
        var beforeLabel = before.ChildElements.OfType<Label>().Single(label => label.FillsRemaining);
        var underLabel = under.ChildElements.OfType<Label>().Single(label => label.FillsRemaining);
        Assert.AreEqual(beforeLabel.Rectangle.Left + (int)Dropdown.HeadingIndent, underLabel.Rectangle.Left);
        Assert.AreEqual(before.Rectangle.Left, under.Rectangle.Left, "The row itself stays full width; only its content is indented.");
        Assert.AreEqual(before.Rectangle.Width, under.Rectangle.Width);
    }

    [TestMethod]
    public void ACheckboxOptionUnderAHeading_IndentsItsCheckboxAndIconAndLabel_ByTheSameStep()
    {
        var harness = new ControlTestHarness();
        var star = new GlyphIcon("*", Color.Red);

        harness.Dropdowns.OpenAt(new Vector2(300, 200), indentsUnderHeadings: true, options:
        [
            new DropdownOption("Before any heading", null, Enabled: true, static () => { }) { IsChecked = false, Icon = star },
            DropdownOption.Header("Group"),
            new DropdownOption("Under it", null, Enabled: true, static () => { }) { IsChecked = true, Icon = star },
        ]);

        var before = (Toggle)Row(harness, 0);
        var under = (Toggle)Row(harness, 2);
        var indent = (int)Dropdown.HeadingIndent;
        Assert.AreEqual(before.CheckboxRectangle().X + indent, under.CheckboxRectangle().X);
        Assert.AreEqual(before.ChildElements.OfType<IconElement>().Single().Rectangle.Left + indent, under.ChildElements.OfType<IconElement>().Single().Rectangle.Left);
        Assert.AreEqual(before.ChildElements.OfType<Label>().Single(label => label.FillsRemaining).Rectangle.Left + indent, under.ChildElements.OfType<Label>().Single(label => label.FillsRemaining).Rectangle.Left);
    }

    [TestMethod]
    public void AHeading_IsNotBold_AndStartsWhereAnOptionBeforeAnyHeadingDoes()
    {
        var harness = new ControlTestHarness();

        harness.Dropdowns.OpenAt(new Vector2(300, 200), indentsUnderHeadings: true, options:
        [
            new DropdownOption("Before any heading", null, Enabled: true, static () => { }),
            DropdownOption.Header("Group"),
            new DropdownOption("Under it", null, Enabled: true, static () => { }),
        ]);

        var heading = harness.Dropdowns.Menu.ChildElements[1].ChildElements.OfType<Label>().Single();
        Assert.AreEqual("Group", heading.Text);
        Assert.IsFalse(heading.IsBold);
        var beforeLabel = Row(harness, 0).ChildElements.OfType<Label>().Single(label => label.FillsRemaining);
        Assert.AreEqual(beforeLabel.Rectangle.Left, heading.Rectangle.Left);
    }

    [TestMethod]
    public void TheIndent_WidensAMenuWhoseWidestOptionIsUnderAHeading()
    {
        var harness = new ControlTestHarness();
        DropdownOption widest = new("Select everything there is", null, Enabled: true, static () => { });
        DropdownOption narrow = new("Cut", null, Enabled: true, static () => { });

        harness.Dropdowns.OpenAt(new Vector2(300, 200), [widest, DropdownOption.Header("A"), narrow], indentsUnderHeadings: true);
        var widthWithWidestFirst = harness.Dropdowns.Menu.Rectangle.Width;

        harness.Dropdowns.OpenAt(new Vector2(300, 200), [narrow, DropdownOption.Header("A"), widest], indentsUnderHeadings: true);

        Assert.AreEqual(widthWithWidestFirst + (int)Dropdown.HeadingIndent, harness.Dropdowns.Menu.Rectangle.Width);
    }

    [TestMethod]
    public void AMenuThatDoesNotIndent_StartsAnOptionUnderAHeadingWhereOneBeforeAnyHeadingStarts()
    {
        var harness = new ControlTestHarness();

        harness.Dropdowns.OpenAt(new Vector2(300, 200),
        [
            new DropdownOption("Before any heading", null, Enabled: true, static () => { }) { IsChecked = false },
            DropdownOption.Header("Group"),
            new DropdownOption("Under it", null, Enabled: true, static () => { }) { IsChecked = false },
        ]);

        var before = (Toggle)Row(harness, 0);
        var under = (Toggle)Row(harness, 2);
        Assert.AreEqual(0f, under.ContentIndent);
        Assert.AreEqual(before.CheckboxRectangle().X, under.CheckboxRectangle().X);
        Assert.AreEqual(before.ChildElements.OfType<Label>().Single(label => label.FillsRemaining).Rectangle.Left, under.ChildElements.OfType<Label>().Single(label => label.FillsRemaining).Rectangle.Left);
    }

    [TestMethod]
    public void MovingThePointerOffTheMenu_ClearsTheHighlightItGave_ButNotOneUpDownGave()
    {
        var harness = new ControlTestHarness();
        var contextMenus = new ContextMenuController(harness.Dropdowns);
        contextMenus.Open(new Vector2(300, 200),
        [
            new DropdownOption("Copy", null, Enabled: true, static () => { }),
            new DropdownOption("Paste", null, Enabled: true, static () => { }),
        ]);
        var menu = harness.Dropdowns.Menu;

        harness.MoveTo(menu.OptionRows[1]!.Rectangle.Center);
        Assert.AreEqual(1, menu.HighlightedIndex);

        harness.MoveTo(new Point(10, 10));
        Assert.AreEqual(-1, menu.HighlightedIndex);

        harness.Press(Keys.Down);
        Assert.AreEqual(0, menu.HighlightedIndex);
        harness.MoveTo(new Point(20, 20));
        Assert.AreEqual(0, menu.HighlightedIndex);
    }

    [TestMethod]
    public void MovingTheHighlight_MovesTheMenusOwnHighlight_NotTheRows()
    {
        var harness = new ControlTestHarness();
        var field = AddField(harness, new Vector2(100, 100), Colours);
        harness.Click(field);

        harness.Press(Keys.Down);

        Assert.AreEqual(1, harness.Dropdowns.Menu.HighlightedIndex);
        Assert.IsTrue(harness.Dropdowns.Menu.OptionRows.All(row => row is Button { ShowsHoverHighlight: false }), "The menu draws which row is current; its rows draw no hover of their own.");
    }
}
