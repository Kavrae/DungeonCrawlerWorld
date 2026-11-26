using Microsoft.Xna.Framework;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>Buttons and toggles show children, and the pointer over a child hovers, presses and clicks the control holding it, through a real UiInputController.</summary>
[TestClass]
[DoNotParallelize]
public sealed class PressableContentTests
{
    private static Button AddButton(ControlTestHarness harness, string text)
    {
        var button = harness.Pool.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(120, 30), DisplayMode = ElementDisplayMode.Fixed },
        });
        button.SetText(text);
        button.Initialize();
        harness.Layers.Add(UiLayer.DynamicHud, button);
        return button;
    }

    private static Label LabelOf(Button button) => button.ChildElements.OfType<Label>().Single();

    [TestMethod]
    public void SetText_ShowsALabelFillingTheButton()
    {
        var harness = new ControlTestHarness();
        var button = AddButton(harness, "Save");

        var label = LabelOf(button);
        Assert.AreEqual("Save", label.Text);
        Assert.AreEqual(button.ContentSize, label.CurrentSize);
        Assert.AreEqual(button.ContentAbsolutePosition, label.AbsolutePosition);
        Assert.AreSame(button.ContentFont, label.Font);
        Assert.IsTrue(label.IsTransparent, "The button's own fill and hover show through it.");
    }

    [TestMethod]
    public void HoveringAndPressingTheLabel_LightsAndPressesTheButton_AndReleasingClicksIt()
    {
        var harness = new ControlTestHarness();
        var button = AddButton(harness, "Save");
        var clicks = 0;
        button.Clicked += _ => clicks++;
        var labelPoint = LabelOf(button).Rectangle.Center;

        harness.MoveTo(labelPoint);
        Assert.IsTrue(button.IsHovered);

        harness.Hold(labelPoint);
        Assert.IsTrue(button.IsPressed);
        Assert.AreEqual(BorderStyle.Inset, button.BorderStyle);
        Assert.AreEqual(0, clicks);

        harness.Release();
        Assert.IsFalse(button.IsPressed);
        Assert.AreEqual(1, clicks);

        harness.MoveTo(new Point(10, 10));
        Assert.IsFalse(button.IsHovered);
    }

    [TestMethod]
    public void ASelectedButton_StaysInset_ThroughAPressAndRelease_AndRisesWhenDeselected()
    {
        var harness = new ControlTestHarness();
        var button = AddButton(harness, "Grid");

        button.IsSelected = true;
        Assert.AreEqual(BorderStyle.Inset, button.BorderStyle);

        harness.Click(button);
        Assert.AreEqual(BorderStyle.Inset, button.BorderStyle);

        button.IsSelected = false;
        Assert.AreEqual(BorderStyle.Outset, button.BorderStyle);
    }

    [TestMethod]
    public void ARaisedTab_ReturnsToItsRestingBevelAfterAPress()
    {
        var harness = new ControlTestHarness();
        var tab = AddButton(harness, "Items");
        tab.RestingBorderStyle = BorderStyle.Inset;
        tab.RestingBorderStyle = BorderStyle.Outset;

        harness.Hold(tab.Rectangle.Center);
        Assert.AreEqual(BorderStyle.Inset, tab.BorderStyle);
        Assert.IsTrue(tab.IsHovered);

        harness.Release();
        Assert.AreEqual(BorderStyle.Outset, tab.BorderStyle);
    }

    [TestMethod]
    public void ADisabledButtonsChildren_TakeNothing()
    {
        var harness = new ControlTestHarness();
        var button = AddButton(harness, "Save");
        var clicks = 0;
        button.Clicked += _ => clicks++;
        button.Enabled = false;
        var labelPoint = LabelOf(button).Rectangle.Center;

        harness.MoveTo(labelPoint);
        Assert.IsFalse(button.IsHovered);

        harness.Click(labelPoint);
        Assert.IsFalse(button.IsPressed);
        Assert.AreEqual(0, clicks);
    }

    [TestMethod]
    public void SetText_Again_ChangesTheOneLabel()
    {
        var harness = new ControlTestHarness();
        var button = AddButton(harness, "_");

        button.SetText("O");

        Assert.AreEqual("O", LabelOf(button).Text);
    }

    [TestMethod]
    public void ShowKeyLabel_PutsAnOutlinedLabelInTheTopLeftCorner_OverTheText()
    {
        var harness = new ControlTestHarness();
        var button = AddButton(harness, "S");

        var keyLabel = button.ShowKeyLabel("O");

        Assert.IsTrue(keyLabel.IsOutlined);
        Assert.AreEqual(button.AbsolutePosition + new Vector2(4, 2), keyLabel.AbsolutePosition);
        Assert.AreSame(keyLabel, button.ChildElements[^1], "Drawn after the text, so over it.");
    }

    [TestMethod]
    public void ClickingAnIconButtonAwayFromItsKeyLabel_KeepsTheKeyLabelOverTheIcon()
    {
        var harness = new ControlTestHarness();
        var button = harness.Pool.CreateElement<Button>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(60, 60), DisplayMode = ElementDisplayMode.Fixed },
        });
        var icon = button.SetIcon(null, "I");
        var keyLabel = button.ShowKeyLabel("I");
        button.Initialize();
        harness.Layers.Add(UiLayer.DynamicHud, button);
        harness.Frame();

        harness.Click(new Point(button.Rectangle.Right - 5, button.Rectangle.Bottom - 5));

        CollectionAssert.AreEqual(new Element[] { icon, keyLabel }, button.ChildElements.ToArray());
    }

    [TestMethod]
    public void ClickingAToggle_OnItsLabel_FlipsIt()
    {
        var harness = new ControlTestHarness();
        var toggle = harness.Pool.CreateElement<Toggle>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(140, 24), DisplayMode = ElementDisplayMode.Fixed },
        });
        var states = new List<bool>();
        toggle.Configure(defaultOn: false, Color.Green, Color.White, states.Add);
        var label = toggle.SetLabel("Freeze", TestFonts.Shared.GetFont(12), Color.Black);
        toggle.Initialize();
        harness.Layers.Add(UiLayer.DynamicHud, toggle);

        Assert.AreEqual(toggle.ContentAbsolutePosition.X + (int)(Toggle.CheckboxSize + Toggle.LabelGap), label.AbsolutePosition.X);

        harness.Click(label.Rectangle.Center);

        Assert.IsTrue(toggle.IsOn);
        CollectionAssert.AreEqual(new[] { true }, states);
    }
}
