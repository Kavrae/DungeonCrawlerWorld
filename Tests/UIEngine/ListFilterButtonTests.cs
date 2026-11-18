using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.UI;
using UIEngine.UI.Filtering;

namespace Tests.UIEngine;

/// <summary>The filter button and its menu, driven through the real input controller over a plain list: counts per row, a flip that keeps the menu open and counts again, the keyboard, and Clear filters.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ListFilterButtonTests
{
    private readonly record struct Fruit(string Kind, bool IsRipe, bool IsBruised);

    private const int RipeRow = 0;
    private const int BruisedRow = 1;
    private const int AppleRow = 2;
    private const int PearRow = 3;
    private const int PlumRow = 4;
    private const int ClearRow = 5;

    private static readonly Fruit[] Basket =
    [
        new("Apple", IsRipe: true, IsBruised: false),
        new("Apple", IsRipe: true, IsBruised: true),
        new("Apple", IsRipe: false, IsBruised: false),
        new("Pear", IsRipe: true, IsBruised: true),
        new("Pear", IsRipe: false, IsBruised: false),
    ];

    private static ListFilter<Fruit> NewFilter() => new(
    [
        new ListFilterGroup<Fruit>("show", "Show only", ListFilterGroupMatch.AllOf,
        [
            new ListFilterChoice<Fruit>("show:ripe", "Ripe", static fruit => fruit.IsRipe),
            new ListFilterChoice<Fruit>("show:bruised", "Bruised", static fruit => fruit.IsBruised),
        ]),
        new ListFilterGroup<Fruit>("kind", "Kind: any of", ListFilterGroupMatch.AnyOf,
        [
            new ListFilterChoice<Fruit>("kind:apple", "Apple", static fruit => fruit.Kind == "Apple"),
            new ListFilterChoice<Fruit>("kind:pear", "Pear", static fruit => fruit.Kind == "Pear"),
            new ListFilterChoice<Fruit>("kind:plum", "Plum", static fruit => fruit.Kind == "Plum"),
        ]),
    ]);

    private static ListFilterButton AddButton(ControlTestHarness harness, ListFilter<Fruit> filter, IReadOnlyList<Fruit>? items = null) =>
        harness.AddRoot<ListFilterButton>(new Vector2(100, 100), new Vector2(90, 24), button => button.Configure(new ListFilterMenuSource<Fruit>(filter, () => items ?? Basket)));

    private static PressableElement Row(ControlTestHarness harness, int row) => harness.Dropdowns.Menu.OptionRows.OfType<PressableElement>().ElementAt(row);

    private static string?[] Counts(ControlTestHarness harness) => [.. Enumerable.Range(RipeRow, PlumRow + 1).Select(row => MenuRows.HotkeyOf(Row(harness, row)))];

    [TestMethod]
    public void Opening_ListsEachHeadingAndChoice_WithTheCountAFlipWouldLeave_AndClearFiltersLast()
    {
        var harness = new ControlTestHarness();
        var button = AddButton(harness, NewFilter());

        harness.Click(button);

        Assert.IsTrue(button.IsMenuOpen);
        CollectionAssert.AreEqual(new[] { "Show only", "Kind: any of" }, MenuRows.HeadingsOf(harness.Dropdowns.Menu).ToArray());
        CollectionAssert.AreEqual(new[] { "3", "2", "3", "2", "0" }, Counts(harness));
        Assert.IsFalse(Row(harness, PlumRow).Enabled, "A choice that would leave nothing can't be checked.");
        Assert.AreEqual(ListFilterButton.ClearFiltersLabel, MenuRows.LabelOf(Row(harness, ClearRow)));
        Assert.IsFalse(Row(harness, ClearRow).Enabled);
        Assert.AreEqual(ListFilterButton.DefaultLabel, button.DrawnLabel);
        Assert.IsTrue(harness.Dropdowns.Menu.OptionRows.OfType<PressableElement>().All(row => row.ContentIndent == 0f), "A filter menu doesn't indent its choices under their headings.");
    }

    [TestMethod]
    public void ChoosingAChoice_ChecksIt_KeepsTheMenuWhereItIs_AndCountsEveryRowAgain()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        var button = AddButton(harness, filter);
        harness.Click(button);
        var menuTop = harness.Dropdowns.Menu.Rectangle.Top;

        harness.Click(Row(harness, RipeRow));
        harness.Frame();

        Assert.IsTrue(filter.Selection.IsChecked(0));
        Assert.IsTrue(button.IsMenuOpen);
        Assert.AreEqual(menuTop, harness.Dropdowns.Menu.Rectangle.Top);
        Assert.IsTrue(((Toggle)Row(harness, RipeRow)).IsOn);
        CollectionAssert.AreEqual(new[] { null, "2", "2", "1", "0" }, Counts(harness), "A checked row shows no count; the others count what checking them would leave.");
        Assert.IsTrue(Row(harness, ClearRow).Enabled);
        Assert.AreEqual(ListFilterButton.LabelFor(ListFilterButton.DefaultLabel, 1), button.DrawnLabel);
        Assert.AreEqual("⛛ 1", button.DrawnLabel);
    }

    [TestMethod]
    public void ACheckedChoice_CanAlwaysBeUnchecked_EvenOverNothing()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        filter.Selection.SetCheckedIds(["show:ripe"]);
        AddButton(harness, filter, items: []);

        harness.Click(harness.Layers[UiLayer.DynamicHud].OfType<ListFilterButton>().Single());

        Assert.IsTrue(Row(harness, RipeRow).Enabled);
        Assert.IsFalse(Row(harness, BruisedRow).Enabled);
        Assert.IsFalse(Row(harness, AppleRow).Enabled);
    }

    [TestMethod]
    public void Keyboard_SpaceOpens_DownAndEnterFlipARow_AndEscapeCloses()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        var button = AddButton(harness, filter);
        harness.Click(button);
        harness.Click(button);
        Assert.IsTrue(button.IsFocused);

        harness.Press(Keys.Space);
        Assert.IsTrue(button.IsMenuOpen);

        harness.Press(Keys.Down);
        harness.Press(Keys.Down);
        harness.Press(Keys.Enter);
        harness.Frame();
        Assert.IsTrue(filter.Selection.IsChecked(1));
        Assert.IsTrue(button.IsMenuOpen);
        Assert.AreEqual(2, harness.Dropdowns.Menu.HighlightedIndex, "The highlight stays on the row that was flipped.");

        harness.Press(Keys.Escape);
        Assert.IsFalse(harness.Dropdowns.IsOpen);
    }

    [TestMethod]
    public void ClearFilters_UnchecksEverything_AndClosesTheMenu()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        filter.Selection.SetCheckedIds(["show:ripe", "kind:pear"]);
        var button = AddButton(harness, filter);

        harness.Click(button);
        harness.Click(Row(harness, ClearRow));

        Assert.IsTrue(filter.Selection.IsEmpty);
        Assert.IsFalse(harness.Dropdowns.IsOpen);
        Assert.AreEqual(ListFilterButton.DefaultLabel, button.DrawnLabel);
    }

    [TestMethod]
    public void RefreshMenu_CountsTheHostsItemsAgain_AfterTheyChanged()
    {
        var harness = new ControlTestHarness();
        var basket = new List<Fruit>(Basket);
        var button = AddButton(harness, NewFilter(), basket);
        harness.Click(button);

        basket.Add(new Fruit("Plum", IsRipe: true, IsBruised: false));
        button.RefreshMenu();
        harness.Frame();

        Assert.AreEqual("1", MenuRows.HotkeyOf(Row(harness, PlumRow)));
        Assert.IsTrue(Row(harness, PlumRow).Enabled);
    }

    [TestMethod]
    public void ClosingTheButton_ClosesItsMenu_AndLetsGoOfTheSelection()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        var button = AddButton(harness, filter);
        harness.Click(button);

        harness.Layers.Remove(UiLayer.DynamicHud, button);
        harness.Pool.CloseElement(button);
        filter.Selection.Flip(0);

        Assert.IsFalse(harness.Dropdowns.IsOpen);
        Assert.AreEqual(ListFilterButton.DefaultLabel, button.DrawnLabel);
    }

    [TestMethod]
    public void ReplacingAnOpenListsOptions_KeepsItsScrollAndHighlight()
    {
        var harness = new ControlTestHarness();
        var field = harness.AddRoot<DropdownField>(new Vector2(100, 100), new Vector2(180, 24), dropdown => dropdown.SetOptions([.. Enumerable.Range(0, 60).Select(index => $"Option {index}")], 0));
        harness.Click(field);
        harness.Press(Keys.Up);
        var menu = harness.Dropdowns.Menu;
        var scroll = menu.ScrollOffset;

        harness.Dropdowns.ReplaceOptions(field, [.. Enumerable.Range(0, 60).Select(index => new DropdownOption($"Renamed {index}", null, Enabled: true, static () => { }))]);
        harness.Frame();

        Assert.AreEqual(59, menu.HighlightedIndex);
        Assert.AreEqual(scroll, menu.ScrollOffset);
        Assert.AreEqual("Renamed 59", MenuRows.LabelOf(Row(harness, 59)));
    }

    [TestMethod]
    public void AChoicesIconAndTooltip_AreItsMenuRows()
    {
        var harness = new ControlTestHarness();
        var ripeIcon = new GlyphIcon("o", Color.Green);
        var filter = new ListFilter<Fruit>(
        [
            new ListFilterGroup<Fruit>("show", "Show only", ListFilterGroupMatch.AllOf,
            [
                new ListFilterChoice<Fruit>("show:ripe", "Ripe", static fruit => fruit.IsRipe) { Icon = ripeIcon, TooltipText = "Ready to eat" },
                new ListFilterChoice<Fruit>("show:bruised", "Bruised", static fruit => fruit.IsBruised),
            ]),
        ]);
        var button = AddButton(harness, filter);

        harness.Click(button);

        Assert.AreEqual(ripeIcon, MenuRows.IconOf(Row(harness, RipeRow)));
        Assert.AreEqual("Ready to eat", Row(harness, RipeRow).TooltipText);
        Assert.AreEqual(GlyphIcon.Blank, MenuRows.IconOf(Row(harness, BruisedRow)));
        Assert.IsNull(Row(harness, BruisedRow).TooltipText);
    }
}
