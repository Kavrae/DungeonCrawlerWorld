using UIEngine.UI.Filtering;

namespace Tests.UIEngine;

/// <summary>The filter model over a plain list of a struct, as any host would hold one: all-of and any-of groups, the counts a flip would leave, and the checked set.</summary>
[TestClass]
public sealed class ListFilterTests
{
    private enum FruitKind
    {
        Apple,
        Pear,
        Plum,
    }

    private readonly record struct Fruit(FruitKind Kind, bool IsRipe, bool IsBruised);

    private const int RipeChoice = 0;
    private const int BruisedChoice = 1;
    private const int AppleChoice = 2;
    private const int PearChoice = 3;
    private const int PlumChoice = 4;

    private static readonly Fruit[] Basket =
    [
        new(FruitKind.Apple, IsRipe: true, IsBruised: false),
        new(FruitKind.Apple, IsRipe: true, IsBruised: true),
        new(FruitKind.Apple, IsRipe: false, IsBruised: false),
        new(FruitKind.Pear, IsRipe: true, IsBruised: true),
        new(FruitKind.Pear, IsRipe: false, IsBruised: false),
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
            new ListFilterChoice<Fruit>("kind:apple", "Apple", static fruit => fruit.Kind == FruitKind.Apple),
            new ListFilterChoice<Fruit>("kind:pear", "Pear", static fruit => fruit.Kind == FruitKind.Pear),
            new ListFilterChoice<Fruit>("kind:plum", "Plum", static fruit => fruit.Kind == FruitKind.Plum),
        ]),
    ]);

    [TestMethod]
    public void NothingChecked_LetsEverythingThrough()
    {
        var filter = NewFilter();

        Assert.AreEqual(Basket.Length, filter.CountMatching(Basket));
        Assert.IsTrue(filter.Selection.IsEmpty);
    }

    [TestMethod]
    public void AnAllOfGroup_ListsOnlyWhatHoldsEveryCheckedChoice()
    {
        var filter = NewFilter();

        filter.Selection.SetChecked(RipeChoice, true);
        Assert.AreEqual(3, filter.CountMatching(Basket));

        filter.Selection.SetChecked(BruisedChoice, true);
        Assert.AreEqual(2, filter.CountMatching(Basket));
        Assert.IsTrue(Basket.Where(filter.Matches).All(fruit => fruit.IsRipe && fruit.IsBruised));
    }

    [TestMethod]
    public void AnAnyOfGroup_ListsWhatHoldsAnyCheckedChoice()
    {
        var filter = NewFilter();

        filter.Selection.SetChecked(PearChoice, true);
        Assert.AreEqual(2, filter.CountMatching(Basket));

        filter.Selection.SetChecked(AppleChoice, true);
        Assert.AreEqual(5, filter.CountMatching(Basket));
    }

    [TestMethod]
    public void TwoGroups_MustBothLetAnItemThrough()
    {
        var filter = NewFilter();

        filter.Selection.SetChecked(RipeChoice, true);
        filter.Selection.SetChecked(AppleChoice, true);

        Assert.AreEqual(2, filter.CountMatching(Basket));
        Assert.IsTrue(Basket.Where(filter.Matches).All(fruit => fruit.IsRipe && fruit.Kind == FruitKind.Apple));
    }

    [TestMethod]
    public void EachChoicesCount_IsWhatAFlipOfItAloneWouldLeave_CheckedOrNot()
    {
        var filter = NewFilter();
        filter.Selection.SetChecked(RipeChoice, true);
        filter.Selection.SetChecked(AppleChoice, true);

        Assert.AreEqual(3, filter.CountMatchingIfFlipped(AppleChoice, Basket));
        Assert.AreEqual(3, filter.CountMatchingIfFlipped(RipeChoice, Basket));
        Assert.AreEqual(1, filter.CountMatchingIfFlipped(BruisedChoice, Basket));
        Assert.AreEqual(3, filter.CountMatchingIfFlipped(PearChoice, Basket));
        Assert.AreEqual(2, filter.Selection.CheckedCount);
        Assert.AreEqual(2, filter.CountMatching(Basket));
    }

    [TestMethod]
    public void AChoiceNothingHolds_WouldLeaveNothing_ButAddedToAnAnyOfChoiceLeavesWhatThatDoes()
    {
        var filter = NewFilter();

        Assert.AreEqual(0, filter.CountMatchingIfFlipped(PlumChoice, Basket));

        filter.Selection.SetChecked(PearChoice, true);
        Assert.AreEqual(2, filter.CountMatchingIfFlipped(PlumChoice, Basket));
    }

    [TestMethod]
    public void AListThatChanges_IsCountedAsItIsWhenAsked()
    {
        var filter = NewFilter();
        filter.Selection.SetChecked(PlumChoice, true);
        var basket = new List<Fruit>(Basket);

        Assert.AreEqual(0, filter.CountMatching(basket));

        basket.Add(new Fruit(FruitKind.Plum, IsRipe: true, IsBruised: false));
        Assert.AreEqual(1, filter.CountMatching(basket));
        Assert.AreEqual(3, filter.CountMatchingIfFlipped(PearChoice, basket));
    }

    [TestMethod]
    public void MatchingAndCounting_AllocateNothing()
    {
        var filter = NewFilter();
        filter.Selection.SetChecked(RipeChoice, true);
        filter.Selection.SetChecked(PearChoice, true);
        IReadOnlyList<Fruit> basket = Basket;
        filter.CountMatching(basket);
        filter.CountMatchingIfFlipped(AppleChoice, basket);
        filter.Matches(Basket[0]);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var matched = 0;
        for (var round = 0; round < 100; round++)
        {
            matched += filter.CountMatching(basket);
            matched += filter.CountMatchingIfFlipped(AppleChoice, basket);
            matched += filter.Matches(Basket[round % Basket.Length]) ? 1 : 0;
        }

        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.IsGreaterThan(0, matched);
    }

    [TestMethod]
    public void TheSelection_ChangesOncePerChange_AndNeverForNoChange()
    {
        var selection = NewFilter().Selection;
        var changes = 0;
        selection.Changed += _ => changes++;

        selection.SetChecked(RipeChoice, true);
        selection.SetChecked(RipeChoice, true);
        selection.Flip(PearChoice);
        selection.SetCheckedIds(["show:ripe", "kind:pear"]);
        selection.Clear();
        selection.Clear();

        Assert.AreEqual(3, changes);
        Assert.AreEqual(3, selection.Version);
        Assert.IsTrue(selection.IsEmpty);
    }

    [TestMethod]
    public void TheCheckedIds_GoOutInChoiceOrder_AndComeBackLeavingOutAnyNoChoiceHas()
    {
        var selection = NewFilter().Selection;
        selection.SetChecked(PlumChoice, true);
        selection.SetChecked(BruisedChoice, true);

        CollectionAssert.AreEqual(new[] { "show:bruised", "kind:plum" }, selection.CheckedIds().ToArray());

        selection.SetCheckedIds(["kind:apple", "kind:quince", "show:ripe"]);
        CollectionAssert.AreEqual(new[] { "show:ripe", "kind:apple" }, selection.CheckedIds().ToArray());
        Assert.AreEqual(2, selection.CheckedCount);
        Assert.AreEqual(-1, selection.IndexOf("kind:quince"));
    }

    [TestMethod]
    public void ChoicesPastSixtyFour_AreCheckedApart()
    {
        var selection = new ListFilterSelection([.. Enumerable.Range(0, 130).Select(index => $"choice:{index}")]);

        selection.SetChecked(129, true);
        selection.SetChecked(64, true);

        Assert.IsTrue(selection.IsChecked(129));
        Assert.IsTrue(selection.IsChecked(64));
        Assert.IsFalse(selection.IsChecked(1));
        Assert.IsFalse(selection.IsChecked(65));
        Assert.AreEqual(2, selection.CheckedCount);
    }

    [TestMethod]
    public void TwoChoicesSharingAnId_AreRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ListFilterSelection(["kind:apple", "kind:apple"]));
    }

    [TestMethod]
    public void EachGroupsChoices_StartWhereThePreviousGroupsEnd()
    {
        var filter = NewFilter();

        Assert.AreEqual(0, filter.FirstChoiceIndexOf(0));
        Assert.AreEqual(AppleChoice, filter.FirstChoiceIndexOf(1));
        Assert.AreEqual("kind:plum", filter.Choices[PlumChoice].Id);
    }
}
