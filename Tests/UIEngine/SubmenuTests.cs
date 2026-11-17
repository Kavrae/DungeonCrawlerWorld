using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>A context menu's submenus open beside the row that chose them, the menus above them staying open, and close back level by level.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SubmenuTests
{
    private static DropdownOption Plain(string label, Action? onSelect = null) => new(label, null, Enabled: true, onSelect ?? (static () => { }));

    /// <summary>The loot box shape: "Grant" opens types, each opening rarities.</summary>
    private static (List<string> Chosen, List<DropdownOption> Options) ThreeLevels()
    {
        var chosen = new List<string>();
        List<DropdownOption> rarities = [Plain("Common", () => chosen.Add("Common")), Plain("Rare", () => chosen.Add("Rare"))];
        List<DropdownOption> types = [DropdownOption.Header("Types"), DropdownOption.Opening("Wooden", rarities), DropdownOption.Opening("Iron", rarities)];
        List<DropdownOption> options = [Plain("Inspect", () => chosen.Add("Inspect")), DropdownOption.Opening("Grant", types), DropdownOption.Opening("Apply", [Plain("Boss")])];
        return (chosen, options);
    }

    private static PressableElement Row(Dropdown menu, int optionIndex) => menu.OptionRows[optionIndex]!;

    private static IReadOnlyList<Dropdown> Open(ControlTestHarness harness) => harness.Dropdowns.OpenMenus;

    [TestMethod]
    public void ChoosingASubmenuRow_OpensItBesideTheRow_TheParentStayingOpenAndHighlightingThatRow()
    {
        var harness = new ControlTestHarness();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        var root = harness.Dropdowns.Menu;

        harness.Click(Row(root, 1));

        Assert.HasCount(2, Open(harness));
        var submenu = Open(harness)[1];
        Assert.IsTrue(root.IsVisible);
        Assert.AreEqual(root.Rectangle.Right, submenu.Rectangle.Left);
        Assert.AreEqual(Row(root, 1).Rectangle.Top, submenu.ChildElements[0].Rectangle.Top, "Its first row (the heading) sits level with the row that opened it.");
        Assert.AreEqual(1, root.OpenSubmenuIndex);

        harness.MoveTo(Row(submenu, 1).Rectangle.Center);
        Assert.AreEqual(1, root.OpenSubmenuIndex, "The row that opened it stays marked while the pointer is in the submenu.");
    }

    [TestMethod]
    public void ThreeLevels_ChoosingTheDeepestOption_RunsItAndClosesEveryMenu()
    {
        var harness = new ControlTestHarness();
        var (chosen, options) = ThreeLevels();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), options);

        harness.Click(Row(harness.Dropdowns.Menu, 1));
        harness.Click(Row(Open(harness)[1], 1));
        Assert.HasCount(3, Open(harness));
        var deepest = Open(harness)[2];
        harness.Click(Row(deepest, 1));

        CollectionAssert.AreEqual(new[] { "Rare" }, chosen);
        Assert.IsFalse(harness.Dropdowns.IsOpen);
        Assert.IsFalse(deepest.IsVisible);
    }

    [TestMethod]
    public void ChoosingAnotherSubmenuRow_SwapsTheSubmenu()
    {
        var harness = new ControlTestHarness();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        var root = harness.Dropdowns.Menu;

        harness.Click(Row(root, 1));
        harness.Click(Row(Open(harness)[1], 1));
        Assert.HasCount(3, Open(harness));

        harness.Click(Row(root, 2));
        Assert.HasCount(2, Open(harness), "The types and rarities closed; Apply's submenu opened in their place.");
        Assert.AreEqual("Boss", MenuRows.LabelOf(Row(Open(harness)[1], 0)));
        Assert.AreEqual(2, root.OpenSubmenuIndex);
    }

    [TestMethod]
    public void ChoosingTheRowWhoseSubmenuIsOpen_ClosesItAndEverythingBelowIt()
    {
        var harness = new ControlTestHarness();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        var root = harness.Dropdowns.Menu;
        harness.Click(Row(root, 1));
        harness.Click(Row(Open(harness)[1], 1));
        Assert.HasCount(3, Open(harness));

        harness.Click(Row(root, 1));

        Assert.HasCount(1, Open(harness));
        Assert.IsTrue(harness.Dropdowns.IsOpen);
        Assert.AreEqual(-1, root.OpenSubmenuIndex);

        harness.Click(Row(root, 1));
        Assert.HasCount(2, Open(harness), "Choosing it again opens it again.");
    }

    [TestMethod]
    public void ClosedSubmenus_GoBackToThePool_SoTheLayerHoldsOnlyTheRootAndWhatIsOpen()
    {
        var harness = new ControlTestHarness();
        var contextMenuLayer = harness.Layers[UiLayer.ContextMenu];
        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        var root = harness.Dropdowns.Menu;

        for (var round = 0; round < 5; round++)
        {
            harness.Click(Row(root, 1));
            harness.Click(Row(Open(harness)[1], 1));
            harness.Click(Row(root, 2));
        }

        Assert.HasCount(2, Open(harness));
        Assert.HasCount(2 + 2, contextMenuLayer, "The root, Apply's open submenu, and the two closed by the last swap until the next open returns them.");

        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        Assert.HasCount(1, Open(harness));
        Assert.HasCount(2, contextMenuLayer, "The two closed before this open went back; Apply's, closed by it, waits for the next.");

        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        CollectionAssert.AreEqual(new Element[] { root }, contextMenuLayer.ToArray());
    }

    [TestMethod]
    public void APressOutsideEveryMenu_ClosesThemAll_AndAPressInsideAnyIsNoDismiss()
    {
        var harness = new ControlTestHarness();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        harness.Click(Row(harness.Dropdowns.Menu, 1));
        harness.Click(Row(Open(harness)[1], 1));

        Assert.IsTrue(harness.Dropdowns.CountsAsInside(Open(harness)[2]));
        Assert.IsTrue(harness.Dropdowns.CountsAsInside(Open(harness)[1]));

        harness.Click(new Point(1500, 900));

        Assert.IsFalse(harness.Dropdowns.IsOpen);
        Assert.IsEmpty(Open(harness));
    }

    [TestMethod]
    public void APressOnAParentsHeading_ClosesOnlyTheMenusBelowIt()
    {
        var harness = new ControlTestHarness();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        harness.Click(Row(harness.Dropdowns.Menu, 1));
        var types = Open(harness)[1];
        harness.Click(Row(types, 1));
        Assert.HasCount(3, Open(harness));

        harness.Click(types.ChildElements[0]);

        Assert.HasCount(2, Open(harness));
        Assert.AreEqual(-1, types.OpenSubmenuIndex);
        Assert.AreEqual(1, types.HighlightedIndex, "The row that had opened it is highlighted again.");
    }

    [TestMethod]
    public void EscapeAndLeft_CloseTheDeepestMenuOnly_EscapeOnTheRootClosesIt()
    {
        var harness = new ControlTestHarness();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);
        harness.Click(Row(harness.Dropdowns.Menu, 1));
        harness.Click(Row(Open(harness)[1], 1));

        harness.Press(Keys.Escape);
        Assert.HasCount(2, Open(harness));

        harness.Press(Keys.Left);
        Assert.HasCount(1, Open(harness));
        Assert.AreEqual(1, harness.Dropdowns.Menu.HighlightedIndex);

        harness.Press(Keys.Left);
        Assert.IsTrue(harness.Dropdowns.IsOpen, "Left on the root is not the menu's.");

        harness.Press(Keys.Escape);
        Assert.IsFalse(harness.Dropdowns.IsOpen);
    }

    [TestMethod]
    public void Keyboard_RightOpensTheHighlightedSubmenu_WithItsFirstOptionHighlighted_AndEnterOpensOneToo()
    {
        var harness = new ControlTestHarness();
        var (chosen, options) = ThreeLevels();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), options);

        harness.Press(Keys.Right);
        Assert.HasCount(1, Open(harness), "Right on an ordinary option does nothing.");

        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        harness.Press(Keys.Right);
        Assert.HasCount(2, Open(harness));
        Assert.AreEqual(1, Open(harness)[1].HighlightedIndex, "The first choosable option, past the heading.");

        harness.Press(Keys.Enter);
        Assert.HasCount(3, Open(harness));
        Assert.AreEqual(0, Open(harness)[2].HighlightedIndex);

        harness.Press(Keys.Down);
        harness.Press(Keys.Enter);
        CollectionAssert.AreEqual(new[] { "Rare" }, chosen);
        Assert.IsFalse(harness.Dropdowns.IsOpen);
    }

    [TestMethod]
    public void AClickOpensASubmenu_WithNothingHighlighted()
    {
        var harness = new ControlTestHarness();
        harness.Dropdowns.OpenAt(new Vector2(300, 200), ThreeLevels().Options);

        harness.Click(Row(harness.Dropdowns.Menu, 1));

        Assert.AreEqual(-1, Open(harness)[1].HighlightedIndex);
    }

    [TestMethod]
    public void LeftAndRight_ReachAFieldsTextBox_WhenNoSubmenuTakesThem()
    {
        var harness = new ControlTestHarness();
        var field = harness.AddRoot<DropdownField>(new Vector2(100, 100), new Vector2(180, 24), field => field.SetOptions(["Red", "Green"], 0));
        harness.Click(field);
        Assert.IsTrue(harness.Dropdowns.IsOpen);

        Assert.IsFalse(harness.Dropdowns.HandleNavigationKey(Keys.Left));
        Assert.IsFalse(harness.Dropdowns.HandleNavigationKey(Keys.Right));
    }

    [TestMethod]
    public void ScrollingAParent_ClosesTheMenusBelowIt()
    {
        var harness = new ControlTestHarness();
        List<DropdownOption> options = [DropdownOption.Opening("More", [Plain("Pick")]), .. Enumerable.Range(0, 80).Select(index => Plain($"Option {index}"))];
        harness.Dropdowns.OpenBelow(harness.AddRoot<Window>(new Vector2(100, 100), new Vector2(180, 24)), options, maximumHeight: 200f);
        var root = harness.Dropdowns.Menu;
        harness.Click(Row(root, 0));
        Assert.HasCount(2, Open(harness));

        harness.Wheel(Row(root, 2).Rectangle.Center, -3);

        Assert.HasCount(1, Open(harness));
    }

    [TestMethod]
    public void PlaceBeside_GoesRightOfTheParent_FlipsLeftAtTheRightEdge_AndMovesUpAtTheBottom()
    {
        var screen = new Rectangle(0, 0, 1000, 800);
        var parent = new Rectangle(100, 100, 200, 300);
        var size = new Vector2(150, 200);

        Assert.AreEqual(new Vector2(300, 150), DropdownController.PlaceBeside(parent, 150, size, screen));

        var nearTheRight = new Rectangle(800, 100, 150, 300);
        Assert.AreEqual(new Vector2(650, 150), DropdownController.PlaceBeside(nearTheRight, 150, size, screen));

        Assert.AreEqual(new Vector2(300, 600), DropdownController.PlaceBeside(parent, 700, size, screen));

        Assert.AreEqual(new Vector2(300, 0), DropdownController.PlaceBeside(parent, 0, new Vector2(150, 900), screen), "Taller than the screen: its top stays on screen.");
    }
}
