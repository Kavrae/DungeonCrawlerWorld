using Engine.Tags;

namespace Tests.Tags;

[TestClass]
public sealed class GameplayTagQueryTests
{
    private static readonly GameplayTag Fire = GameplayTag.Get("GameplayTagQueryTests.Damage.Fire");
    private static readonly GameplayTag Lava = GameplayTag.Get("GameplayTagQueryTests.Damage.Fire.Lava");
    private static readonly GameplayTag Poison = GameplayTag.Get("GameplayTagQueryTests.Damage.Poison");
    private static readonly GameplayTag Magic = GameplayTag.Get("GameplayTagQueryTests.Magic");
    private static readonly GameplayTag Melee = GameplayTag.Get("GameplayTagQueryTests.Delivery.Melee");

    [TestMethod]
    public void All_NeedsEveryTag_ParentAware()
    {
        var magicalFire = GameplayTagQuery.All([Fire, Magic]);

        Assert.IsTrue(magicalFire.Matches([Lava, Magic]));
        Assert.IsFalse(magicalFire.Matches([Lava, Melee]));
        Assert.IsFalse(magicalFire.Matches([Magic]));
    }

    [TestMethod]
    public void Any_NeedsOneTag_ParentAware()
    {
        var fireOrPoison = GameplayTagQuery.Any([Fire, Poison]);

        Assert.IsTrue(fireOrPoison.Matches([Lava]));
        Assert.IsTrue(fireOrPoison.Matches([Poison, Melee]));
        Assert.IsFalse(fireOrPoison.Matches([Magic]));
        Assert.IsFalse(fireOrPoison.Matches(GameplayTagSet.Empty));
    }

    [TestMethod]
    public void None_RejectsEveryTag_ParentAware()
    {
        var notFire = GameplayTagQuery.None([Fire]);

        Assert.IsTrue(notFire.Matches([Poison]));
        Assert.IsTrue(notFire.Matches(GameplayTagSet.Empty));
        Assert.IsFalse(notFire.Matches([Lava]));
    }

    [TestMethod]
    public void AllParts_MustHoldTogether()
    {
        var query = new GameplayTagQuery(requireAll: [Magic], requireAny: [Fire, Poison], exclude: [Melee]);

        Assert.IsTrue(query.Matches([Magic, Lava]));
        Assert.IsFalse(query.Matches([Lava]));
        Assert.IsFalse(query.Matches([Magic]));
        Assert.IsFalse(query.Matches([Magic, Poison, Melee]));
    }

    [TestMethod]
    public void AnEmptyQuery_MatchesEverySet()
    {
        var query = new GameplayTagQuery();

        Assert.IsTrue(query.Matches(GameplayTagSet.Empty));
        Assert.IsTrue(query.Matches([Fire, Melee]));
    }
}
