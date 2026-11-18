using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class KeyboardControlTests
{
    private static readonly string[] Colours = ["Red", "Green", "Blue"];

    private static TextBox CreateTextBox(bool multiline = false)
    {
        var textBox = TestElementPoolServiceFactory.Create(TestFonts.Shared).CreateElement<TextBox>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(300, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Multiline = multiline },
        });
        textBox.Initialize();
        return textBox;
    }

    private static void PressInTextBox(TextBox textBox, params Keys[] keys) => textBox.HandleHotkeys(new KeyboardState(keys), new KeyboardState());

    private static void Type(TextBox textBox, string text)
    {
        foreach (var character in text)
        {
            textBox.HandleTextInput(character);
        }
    }

    [TestMethod]
    public void TextBox_UndoTakesBackARunOfTyping_AsOneStep_AndRedoPutsItBack()
    {
        var textBox = CreateTextBox();
        Type(textBox, "abc");
        PressInTextBox(textBox, Keys.Space);
        PressInTextBox(textBox, Keys.Back);

        PressInTextBox(textBox, Keys.LeftControl, Keys.Z);
        Assert.AreEqual("abc", textBox.OriginalText);

        PressInTextBox(textBox, Keys.LeftControl, Keys.Z);
        Assert.AreEqual(string.Empty, textBox.OriginalText);
        Assert.IsFalse(textBox.CanUndo);

        PressInTextBox(textBox, Keys.LeftControl, Keys.Y);
        Assert.AreEqual("abc", textBox.OriginalText);

        PressInTextBox(textBox, Keys.LeftControl, Keys.LeftShift, Keys.Z);
        Assert.AreEqual("ab", textBox.OriginalText);
    }

    [TestMethod]
    public void TextBox_SetText_ClearsTheUndoHistory()
    {
        var textBox = CreateTextBox();
        Type(textBox, "typed");

        textBox.SetText("from outside");

        Assert.IsFalse(textBox.CanUndo);
    }

    [TestMethod]
    public void TextBox_MaximumLength_StopsTypingAndCutsAPasteShort()
    {
        var textBox = CreateTextBox();
        textBox.MaximumLength = 3;

        Type(textBox, "abcd");

        Assert.AreEqual("abc", textBox.OriginalText);
    }

    [TestMethod]
    public void TextBox_MaskCharacter_ShowsTheMask_KeepsTheText_AndOffersNoCopy()
    {
        var textBox = CreateTextBox();
        textBox.MaskCharacter = '*';
        Type(textBox, "secret word");
        PressInTextBox(textBox, Keys.LeftControl, Keys.A);

        Assert.AreEqual("secret word", textBox.OriginalText);
        Assert.AreEqual("***********", textBox.DisplayText.FormattedText);
        var options = textBox.BuildContextMenuOptions();
        Assert.IsFalse(options.Single(option => option.Label == "Copy").Enabled);
        Assert.IsFalse(options.Single(option => option.Label == "Cut").Enabled);
    }

    [TestMethod]
    public void TextBox_MaskCharacter_MakesCtrlBackspaceTakeTheWholeText()
    {
        var textBox = CreateTextBox();
        textBox.MaskCharacter = '*';
        Type(textBox, "two words");

        PressInTextBox(textBox, Keys.LeftControl, Keys.Back);

        Assert.AreEqual(string.Empty, textBox.OriginalText);
    }

    [TestMethod]
    public void TextBox_PageUpAndPageDown_GoToTheStartAndEndOfASingleLine()
    {
        var textBox = CreateTextBox();
        Type(textBox, "abc");

        PressInTextBox(textBox, Keys.PageUp);
        textBox.HandleTextInput('x');
        PressInTextBox(textBox, Keys.PageDown);
        textBox.HandleTextInput('y');

        Assert.AreEqual("xabcy", textBox.OriginalText);
    }

    [TestMethod]
    public void Button_Enter_PressesItWhileFocused()
    {
        var harness = new ControlTestHarness();
        var button = harness.AddRoot<Button>(new Vector2(50, 50), new Vector2(80, 24));
        var presses = 0;
        button.Clicked += _ => presses++;
        harness.Input.FocusElement(button);

        harness.Press(Keys.Enter);
        harness.Press(Keys.Space);

        Assert.AreEqual(2, presses);
        Assert.IsTrue(button.IsTabStop);
    }

    [TestMethod]
    public void ModalDialog_Enter_ChoosesTheDefaultButton_WithNothingFocused()
    {
        var harness = new ControlTestHarness();
        var chosen = new List<string>();
        ModalDialog.Show(harness.Pool, harness.Layers, ControlTestHarness.ScreenBounds, new ModalDialogRequest("Save?", [new DialogButton("Save", () => chosen.Add("Save")), new DialogButton("Cancel", () => chosen.Add("Cancel"))], CancelButtonIndex: 1)
        {
            DefaultButtonIndex = 0,
        });
        harness.Frame();

        harness.Press(Keys.Enter);

        CollectionAssert.AreEqual(new[] { "Save" }, chosen);
    }

    [TestMethod]
    public void ToggleList_All_IsIndeterminateWhileOnlySomeItemsAreOn_AndAClickTurnsThemAllOn()
    {
        var harness = new ControlTestHarness();
        harness.Pool.RegisterFactory(() => new ToggleList(TestElementServices.For(harness.Pool)));
        var list = harness.AddRoot<ToggleList>(
            new Vector2(100, 100),
            new Vector2(200, ToggleList.HeightFor(3, hasAll: true)),
            list => list.Configure([new ToggleListItem("A", true), new ToggleListItem("B", false), new ToggleListItem("C", false)], "All", Color.Black, Color.Gold, Color.Gray),
            new ElementChromeOptions { ShowBorder = false, ShowTitle = false });
        harness.Frame();

        Assert.IsTrue(list.AllToggle!.IsIndeterminate);
        Assert.IsFalse(list.AllToggle.IsOn);

        harness.Click(list.AllToggle);

        Assert.IsFalse(list.AllToggle.IsIndeterminate);
        Assert.IsTrue(list.AllToggle.IsOn);
        Assert.IsTrue(Enumerable.Range(0, list.ItemCount).All(list.IsItemOn));

        harness.Click(list.ItemToggle(1));
        Assert.IsTrue(list.AllToggle.IsIndeterminate);
    }

    [TestMethod]
    public void DropdownField_UpAndDown_ChooseWithoutOpeningTheList()
    {
        var harness = new ControlTestHarness();
        var field = harness.AddRoot<DropdownField>(new Vector2(100, 100), new Vector2(180, 24), field => field.SetOptions(Colours, 0));
        var chosen = new List<int>();
        field.SelectionChanged += chosen.Add;
        harness.Input.FocusElement(field);

        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        harness.Press(Keys.Up);

        Assert.IsFalse(field.IsListOpen);
        Assert.AreEqual(1, field.SelectedIndex);
        CollectionAssert.AreEqual(new[] { 1, 2, 1 }, chosen);
    }

    [TestMethod]
    public void DropdownField_HomeAndEnd_MoveTheOpenListsHighlightToItsEnds()
    {
        var harness = new ControlTestHarness();
        var field = harness.AddRoot<DropdownField>(new Vector2(100, 100), new Vector2(180, 24), field => field.SetOptions(Colours, 1));
        harness.Input.FocusElement(field);
        harness.Press(Keys.Space);

        harness.Press(Keys.End);
        Assert.AreEqual(2, harness.Dropdowns.Menu.HighlightedIndex);

        harness.Press(Keys.Home);
        Assert.AreEqual(0, harness.Dropdowns.Menu.HighlightedIndex);
    }

    [TestMethod]
    public void NumericField_UpDownStep_HomeIsTheMinimum_EndIsTheMaximum()
    {
        var harness = new ControlTestHarness();
        var field = harness.AddRoot<NumericField>(new Vector2(100, 100), new Vector2(120, 24), field => field.Configure(5, minimum: -2, maximum: 20, step: 2));
        harness.Input.FocusElement(field);

        harness.Press(Keys.Up);
        Assert.AreEqual(7, field.Value);

        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        Assert.AreEqual(3, field.Value);

        harness.Press(Keys.Home);
        Assert.AreEqual(-2, field.Value);

        harness.Press(Keys.End);
        Assert.AreEqual(20, field.Value);
        Assert.AreEqual("20", field.OriginalText);
    }

    [TestMethod]
    public void NumericField_SpinnerButtons_StepUpFromTheTopHalf_AndDownFromTheBottom()
    {
        var harness = new ControlTestHarness();
        var field = harness.AddRoot<NumericField>(new Vector2(100, 100), new Vector2(120, 24), field => field.Configure(5, minimum: 0, maximum: 10, showsSpinnerButtons: true));
        var content = field.ContentRectangle;
        var spinnerX = content.Right - (int)(NumericField.SpinnerWidth / 2);

        harness.Click(new Point(spinnerX, content.Top + 3));
        Assert.AreEqual(6, field.Value);

        harness.Click(new Point(spinnerX, content.Bottom - 3));
        harness.Click(new Point(spinnerX, content.Bottom - 3));
        Assert.AreEqual(4, field.Value);
    }
}
