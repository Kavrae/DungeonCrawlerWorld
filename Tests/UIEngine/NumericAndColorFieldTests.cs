using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class NumericAndColorFieldTests
{
    private static readonly Vector2 FieldSize = new(120, 24);

    private static NumericField AddNumber(ControlTestHarness harness, double value, double minimum, double maximum, double step = 1, int decimalPlaces = 0) =>
        harness.AddRoot<NumericField>(new Vector2(100, 100), FieldSize, field => field.Configure(value, minimum, maximum, step, decimalPlaces));

    private static void ReplaceText(ControlTestHarness harness, TextBox box, string text)
    {
        harness.Click(box);
        box.HandleHotkeys(new KeyboardState(Keys.LeftControl, Keys.A), new KeyboardState());
        harness.Type(text);
    }

    [TestMethod]
    public void CommitTypedText_TakesWhatIsTyped_WhileTheFieldStillHasFocus()
    {
        var harness = new ControlTestHarness();
        var field = AddNumber(harness, value: 1, minimum: -100, maximum: 100);
        var changes = new List<double>();
        field.ValueChanged += changes.Add;

        ReplaceText(harness, field, "-42");
        field.CommitTypedText();

        Assert.AreEqual(-42d, field.Value);
        CollectionAssert.AreEqual(new[] { -42d }, changes);
    }

    [TestMethod]
    public void TheWheel_StepsTheValue_WithinItsLimits()
    {
        var harness = new ControlTestHarness();
        var field = AddNumber(harness, value: 8, minimum: 0, maximum: 10, step: 1);
        var changes = new List<double>();
        field.ValueChanged += changes.Add;

        harness.Wheel(field.Rectangle.Center, notches: 1);
        harness.Wheel(field.Rectangle.Center, notches: 5);
        harness.Wheel(field.Rectangle.Center, notches: 1);

        Assert.AreEqual(10d, field.Value);
        Assert.AreEqual("10", field.OriginalText);
        CollectionAssert.AreEqual(new[] { 9d, 10d }, changes, "Clamped at the maximum; a step that changes nothing raises nothing.");
    }

    [TestMethod]
    public void TheWheel_OverAFieldInAScrollingWindow_StepsTheValue_AndLeavesTheWindowWhereItIs()
    {
        var harness = new ControlTestHarness();
        var window = harness.AddRoot<Window>(new Vector2(50, 50), new Vector2(200, 100), chrome: new ElementChromeOptions { ShowBorder = true, CanUserScrollVertical = true });
        var field = harness.Pool.CreateElement<NumericField>(window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = FieldSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
        });
        field.Configure(1, 0, 5);
        window.AddChild(field);
        var filler = harness.Pool.CreateElement<Window>(window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 40), Size = new Vector2(100, 400), DisplayMode = ElementDisplayMode.Fixed },
        });
        window.AddChild(filler);

        harness.Wheel(field.Rectangle.Center, notches: -1);

        Assert.AreEqual(0d, field.Value);
        Assert.AreEqual(0f, window.ScrollOffset.Y);

        harness.Wheel(filler.Rectangle.Center, notches: -1);
        Assert.IsGreaterThan(0f, window.ScrollOffset.Y, "Anywhere else, the wheel still scrolls the window.");
    }

    [TestMethod]
    public void TypedText_IsReadOnEnter_ClampedAndRounded()
    {
        var harness = new ControlTestHarness();
        var field = AddNumber(harness, value: 1, minimum: 0, maximum: 5, step: 0.5, decimalPlaces: 1);

        ReplaceText(harness, field, "3.26");
        harness.Press(Keys.Enter);
        Assert.AreEqual(3.3d, field.Value);
        Assert.AreEqual("3.3", field.OriginalText);

        ReplaceText(harness, field, "99");
        harness.Press(Keys.Enter);
        Assert.AreEqual(5d, field.Value);
    }

    [TestMethod]
    public void Typing_TakesOnlyWhatANumberIsWrittenWith()
    {
        var harness = new ControlTestHarness();
        var field = AddNumber(harness, value: 0, minimum: 0, maximum: 100);

        ReplaceText(harness, field, "4a-.2");

        Assert.AreEqual("42", field.OriginalText, "No letters, no minus when the minimum isn't negative, no point for a whole number.");
    }

    [TestMethod]
    public void TextThatIsNotANumber_PutsTheLastValueBack_WhenFocusLeaves()
    {
        var harness = new ControlTestHarness();
        var field = AddNumber(harness, value: 7, minimum: -10, maximum: 10);
        var other = AddNumber(harness, value: 0, minimum: 0, maximum: 1);
        other.SetRelativePosition(new Vector2(100, 200));

        ReplaceText(harness, field, "-");
        harness.Click(other);

        Assert.AreEqual(7d, field.Value);
        Assert.AreEqual("7", field.OriginalText);
    }

    [TestMethod]
    public void AColour_TypedAsHex_IsReadWhenFocusLeaves()
    {
        var harness = new ControlTestHarness();
        var field = harness.AddRoot<ColorField>(new Vector2(100, 100), FieldSize, colour => colour.Configure(Color.White));
        var other = AddNumber(harness, value: 0, minimum: 0, maximum: 1);
        other.SetRelativePosition(new Vector2(100, 200));
        var changes = new List<Color>();
        field.ValueChanged += changes.Add;
        Assert.AreEqual("#FFFFFF", field.OriginalText);

        ReplaceText(harness, field, "#ff800040");
        harness.Click(other);

        Assert.AreEqual(new Color(255, 128, 0, 64), field.Value);
        Assert.AreEqual("#FF800040", field.OriginalText);
        CollectionAssert.AreEqual(new[] { new Color(255, 128, 0, 64) }, changes);
    }

    [TestMethod]
    public void ColourText_ReadsSixOrEightDigits_AndNothingElse()
    {
        Assert.IsTrue(ColorField.TryParse("102030", out var opaque));
        Assert.AreEqual(new Color(16, 32, 48, 255), opaque);
        Assert.IsTrue(ColorField.TryParse("#10203040", out var translucent));
        Assert.AreEqual(new Color(16, 32, 48, 64), translucent);
        Assert.IsFalse(ColorField.TryParse("#1234", out _));
        Assert.AreEqual("#102030", ColorField.Format(opaque));
        Assert.AreEqual("#10203040", ColorField.Format(translucent));
    }
}
