using Engine.Tags;

namespace Tests.Tags;

[TestClass]
public sealed class GameplayTagSetTests
{
    private static readonly GameplayTag Damage = GameplayTag.Get("GameplayTagSetTests.Damage");
    private static readonly GameplayTag Fire = GameplayTag.Get("GameplayTagSetTests.Damage.Fire");
    private static readonly GameplayTag Lava = GameplayTag.Get("GameplayTagSetTests.Damage.Fire.Lava");
    private static readonly GameplayTag Poison = GameplayTag.Get("GameplayTagSetTests.Damage.Poison");
    private static readonly GameplayTag Magic = GameplayTag.Get("GameplayTagSetTests.Magic");
    private static readonly GameplayTag Melee = GameplayTag.Get("GameplayTagSetTests.Delivery.Melee");

    [TestMethod]
    public void Has_IsParentAware_HasExact_IsNot()
    {
        GameplayTagSet tags = [Lava];

        Assert.IsTrue(tags.Has(Lava));
        Assert.IsTrue(tags.Has(Fire));
        Assert.IsTrue(tags.Has(Damage));
        Assert.IsTrue(tags.HasExact(Lava));
        Assert.IsFalse(tags.HasExact(Fire));
        Assert.IsFalse(tags.HasExact(Damage));
    }

    [TestMethod]
    public void Has_AChildOfAHeldTag_IsFalse()
    {
        GameplayTagSet tags = [Fire];

        Assert.IsFalse(tags.Has(Lava));
        Assert.IsFalse(tags.Has(Poison));
    }

    [TestMethod]
    public void Has_None_IsFalse()
    {
        GameplayTagSet tags = [Fire];

        Assert.IsFalse(tags.Has(GameplayTag.None));
        Assert.IsFalse(tags.HasExact(GameplayTag.None));
    }

    [TestMethod]
    public void HasAny_IsParentAware_HasAnyExact_IsNot()
    {
        GameplayTagSet tags = [Lava, Melee];

        Assert.IsTrue(tags.HasAny([Poison, Fire]));
        Assert.IsFalse(tags.HasAny([Poison, Magic]));
        Assert.IsTrue(tags.HasAnyExact([Poison, Lava]));
        Assert.IsFalse(tags.HasAnyExact([Poison, Fire]));
    }

    [TestMethod]
    public void HasAll_IsParentAware_HasAllExact_IsNot()
    {
        GameplayTagSet tags = [Lava, Magic];

        Assert.IsTrue(tags.HasAll([Fire, Magic]));
        Assert.IsFalse(tags.HasAll([Fire, Melee]));
        Assert.IsTrue(tags.HasAllExact([Lava, Magic]));
        Assert.IsFalse(tags.HasAllExact([Fire, Magic]));
    }

    [TestMethod]
    public void HasNone_IsParentAware_HasNoneExact_IsNot()
    {
        GameplayTagSet tags = [Lava];

        Assert.IsFalse(tags.HasNone([Fire]));
        Assert.IsTrue(tags.HasNone([Poison, Magic]));
        Assert.IsTrue(tags.HasNoneExact([Fire]));
        Assert.IsFalse(tags.HasNoneExact([Lava]));
    }

    [TestMethod]
    public void AgainstAnEmptySet_AnyIsFalse_AllAndNoneAreTrue()
    {
        GameplayTagSet tags = [Fire];

        Assert.IsFalse(tags.HasAny(GameplayTagSet.Empty));
        Assert.IsFalse(tags.HasAnyExact(GameplayTagSet.Empty));
        Assert.IsTrue(tags.HasAll(GameplayTagSet.Empty));
        Assert.IsTrue(tags.HasAllExact(GameplayTagSet.Empty));
        Assert.IsTrue(tags.HasNone(GameplayTagSet.Empty));
        Assert.IsTrue(tags.HasNoneExact(GameplayTagSet.Empty));
    }

    [TestMethod]
    public void TheDefaultSet_IsEmpty_AndAnswersEveryQuery()
    {
        var tags = default(GameplayTagSet);

        Assert.IsTrue(tags.IsEmpty);
        Assert.AreEqual(0, tags.Count);
        Assert.AreEqual(GameplayTagSet.Empty, tags);
        Assert.IsFalse(tags.Has(Fire));
        Assert.IsFalse(tags.HasExact(Fire));
        Assert.IsFalse(tags.HasAny([Fire]));
        Assert.IsFalse(tags.HasAll([Fire]));
        Assert.IsTrue(tags.HasAll(GameplayTagSet.Empty));
        Assert.IsTrue(tags.HasNone([Fire]));
        Assert.IsEmpty(Enumerate(tags));
        Assert.AreEqual("[]", tags.ToString());
    }

    [TestMethod]
    public void AnEmptyCollectionExpression_IsTheEmptySet()
    {
        GameplayTagSet tags = [];

        Assert.IsTrue(tags.IsEmpty);
        Assert.AreEqual(default, tags);
    }

    [TestMethod]
    public void Duplicates_Collapse_AndOrderIsKept()
    {
        GameplayTagSet tags = [Magic, Fire, Magic, Melee, Fire];

        Assert.AreEqual(3, tags.Count);
        CollectionAssert.AreEqual(new[] { Magic, Fire, Melee }, Enumerate(tags));
    }

    [TestMethod]
    public void AParentAndItsChild_AreBothKept()
    {
        GameplayTagSet tags = [Fire, Lava];

        Assert.AreEqual(2, tags.Count);
    }

    [TestMethod]
    public void Create_WithNone_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => GameplayTagSet.Create([Fire, GameplayTag.None]));
    }

    [TestMethod]
    public void Equality_IgnoresOrder()
    {
        GameplayTagSet first = [Fire, Magic, Melee];
        GameplayTagSet second = [Melee, Fire, Magic];

        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    [TestMethod]
    public void Equality_IsExact_NotParentAware()
    {
        GameplayTagSet lava = [Lava];
        GameplayTagSet fire = [Fire];
        GameplayTagSet fireAndMagic = [Fire, Magic];

        Assert.AreNotEqual(lava, fire);
        Assert.IsTrue(fire != fireAndMagic);
    }

    [TestMethod]
    public void With_AddsATagAtTheEnd_AndLeavesTheOriginal()
    {
        GameplayTagSet tags = [Fire];

        var withMagic = tags.With(Magic);

        CollectionAssert.AreEqual(new[] { Fire, Magic }, Enumerate(withMagic));
        CollectionAssert.AreEqual(new[] { Fire }, Enumerate(tags));
        Assert.AreEqual(tags, tags.With(Fire));
        CollectionAssert.AreEqual(new[] { Magic }, Enumerate(GameplayTagSet.Empty.With(Magic)));
    }

    [TestMethod]
    public void With_None_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => GameplayTagSet.Empty.With(GameplayTag.None));
    }

    [TestMethod]
    public void Union_AppendsWhatIsNotAlreadyHeldExactly()
    {
        GameplayTagSet tags = [Fire, Melee];

        CollectionAssert.AreEqual(new[] { Fire, Melee, Magic, Lava }, Enumerate(tags.Union([Magic, Fire, Lava])));
        Assert.AreEqual(tags, tags.Union(GameplayTagSet.Empty));
        Assert.AreEqual(tags, GameplayTagSet.Empty.Union(tags));
    }

    [TestMethod]
    public void Without_RemovesOnlyThatTag()
    {
        GameplayTagSet tags = [Fire, Lava, Magic];

        CollectionAssert.AreEqual(new[] { Lava, Magic }, Enumerate(tags.Without(Fire)));
        Assert.AreEqual(tags, tags.Without(Poison));
    }

    [TestMethod]
    public void WithoutDescendantsOf_RemovesTheTagAndEverythingUnderIt()
    {
        GameplayTagSet tags = [Fire, Lava, Poison, Magic];

        CollectionAssert.AreEqual(new[] { Poison, Magic }, Enumerate(tags.WithoutDescendantsOf(Fire)));
        CollectionAssert.AreEqual(new[] { Magic }, Enumerate(tags.WithoutDescendantsOf(Damage)));
        Assert.IsTrue(tags.WithoutDescendantsOf(Damage).WithoutDescendantsOf(Magic).IsEmpty);
    }

    [TestMethod]
    public void ToString_ListsFullNamesInOrder()
    {
        GameplayTagSet tags = [Magic, Fire];

        Assert.AreEqual("[GameplayTagSetTests.Magic, GameplayTagSetTests.Damage.Fire]", tags.ToString());
    }

    private static List<GameplayTag> Enumerate(GameplayTagSet tags)
    {
        var enumerated = new List<GameplayTag>();
        foreach (var tag in tags)
        {
            enumerated.Add(tag);
        }

        return enumerated;
    }
}
