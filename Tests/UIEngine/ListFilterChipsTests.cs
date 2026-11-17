using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.Filtering;

namespace Tests.UIEngine;

/// <summary>The chip row over a plain filter: no height while nothing is checked, a chip per checked choice with Clear last, wrapping within its width, and a click removing what it names.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ListFilterChipsTests
{
    private readonly record struct Fruit(string Kind, bool IsRipe, bool IsBruised);

    private static readonly Fruit[] Basket = [new("Apple", IsRipe: true, IsBruised: false), new("Pear", IsRipe: false, IsBruised: true)];

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
        ]),
    ]);

    private static ListFilterChips AddChips(ControlTestHarness harness, ListFilter<Fruit> filter, float width = 400f) =>
        harness.AddRoot<ListFilterChips>(new Vector2(100, 100), new Vector2(width, 0), chips => chips.Configure(new ListFilterMenuSource<Fruit>(filter, () => Basket), width), new ElementChromeOptions { ShowBorder = false });

    private static Button Chip(ListFilterChips chips, string label) => chips.ChildElements.OfType<Button>().Single(chip => chip.LeftText == label);

    [TestMethod]
    public void NothingChecked_HasNoHeight_AndIsHidden()
    {
        var harness = new ControlTestHarness();

        var chips = AddChips(harness, NewFilter());

        Assert.IsFalse(chips.IsVisible);
        Assert.AreEqual(0f, chips.CurrentSize.Y);
        Assert.AreEqual(0, chips.ChipCount);
    }

    [TestMethod]
    public void EachCheckedChoice_IsAChipWithItsX_ThenClear_OnOneLine()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        var chips = AddChips(harness, filter);
        var resized = 0;
        chips.Resized += _ => resized++;

        filter.Selection.SetCheckedIds(["show:ripe", "kind:pear"]);

        Assert.IsTrue(chips.IsVisible);
        CollectionAssert.AreEqual(new[] { "Ripe", "Pear", ListFilterChips.ClearLabel }, chips.ChildElements.OfType<Button>().Select(chip => chip.LeftText).ToArray());
        Assert.AreEqual(ListFilterChips.RemoveMarker, Chip(chips, "Ripe").RightText);
        Assert.AreEqual(ListFilterChips.RemoveTooltip, Chip(chips, "Ripe").TooltipText);
        Assert.IsNull(Chip(chips, ListFilterChips.ClearLabel).RightText);
        Assert.AreEqual(ListFilterChips.ChipHeight, chips.CurrentSize.Y);
        Assert.IsGreaterThan(0, resized);
    }

    [TestMethod]
    public void ChipsPastTheWidth_WrapOntoMoreLines_AndTheHeightFollows()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        var chips = AddChips(harness, filter, width: 120f);

        filter.Selection.SetCheckedIds(["show:ripe", "show:bruised", "kind:apple", "kind:pear"]);

        Assert.IsGreaterThan(ListFilterChips.ChipHeight, chips.CurrentSize.Y);
        Assert.AreEqual(chips.MeasureHeight(120f), chips.CurrentSize.Y);
        Assert.IsTrue(chips.ChildElements.All(chip => chip.RelativePosition.X + chip.CurrentSize.X <= 120f || chip.RelativePosition.X == 0f));

        chips.SetWidth(1000f);
        Assert.AreEqual(ListFilterChips.ChipHeight, chips.CurrentSize.Y);
    }

    [TestMethod]
    public void ClickingAChip_UnchecksItsChoiceAlone()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        filter.Selection.SetCheckedIds(["show:ripe", "kind:pear"]);
        var chips = AddChips(harness, filter);

        harness.Click(Chip(chips, "Ripe"));
        harness.Frame();

        CollectionAssert.AreEqual(new[] { "kind:pear" }, filter.Selection.CheckedIds().ToArray());
        Assert.AreEqual(1, chips.ChipCount);
    }

    [TestMethod]
    public void ClickingClear_UnchecksEverything_AndTheRowGoes()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        filter.Selection.SetCheckedIds(["show:ripe", "kind:pear"]);
        var chips = AddChips(harness, filter);

        harness.Click(Chip(chips, ListFilterChips.ClearLabel));
        harness.Frame();

        Assert.IsTrue(filter.Selection.IsEmpty);
        Assert.IsFalse(chips.IsVisible);
        Assert.AreEqual(0f, chips.CurrentSize.Y);
    }

    [TestMethod]
    public void ClosingTheRow_LetsGoOfTheSelection()
    {
        var harness = new ControlTestHarness();
        var filter = NewFilter();
        var chips = AddChips(harness, filter);

        harness.Layers.Remove(UiLayer.DynamicHud, chips);
        harness.Pool.CloseElement(chips);
        filter.Selection.SetCheckedIds(["show:ripe"]);

        Assert.AreEqual(0, chips.ChipCount);
        Assert.IsEmpty(chips.ChildElements);
    }

    [TestMethod]
    public void AChoiceWithAnIconAndTooltip_IsAChipShowingBoth_WiderByTheIcon()
    {
        var harness = new ControlTestHarness();
        var ripeIcon = new GlyphIcon("o", Color.Green);
        var filter = new ListFilter<Fruit>(
        [
            new ListFilterGroup<Fruit>("show", "Show only", ListFilterGroupMatch.AllOf,
            [
                new ListFilterChoice<Fruit>("show:ripe", "Ripe", static fruit => fruit.IsRipe) { Icon = ripeIcon, TooltipText = "Ready to eat" },
            ]),
        ]);
        var plainFilter = NewFilter();
        var plainChips = AddChips(harness, plainFilter);
        var chips = AddChips(harness, filter);

        filter.Selection.SetCheckedIds(["show:ripe"]);
        plainFilter.Selection.SetCheckedIds(["show:ripe"]);

        Assert.AreEqual(Chip(plainChips, "Ripe").Rectangle.Width + (int)Button.LeadingIconWidth, Chip(chips, "Ripe").Rectangle.Width);
        Assert.AreEqual(ListFilterChips.RemoveTooltip, Chip(plainChips, "Ripe").TooltipText);
        Assert.AreEqual(ripeIcon, Chip(chips, "Ripe").LeadingIcon);
        Assert.AreEqual("Ready to eat", Chip(chips, "Ripe").TooltipText);
        Assert.IsNull(Chip(chips, ListFilterChips.ClearLabel).LeadingIcon);
    }
}
