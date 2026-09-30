using Engine.Tags;

namespace Tests.Tags;

[TestClass]
public sealed class GameplayTagTests
{
    [TestMethod]
    public void Get_TheSameName_ReturnsTheSameTag()
    {
        Assert.AreEqual(GameplayTag.Get("GameplayTagTests.Same.Name"), GameplayTag.Get("GameplayTagTests.Same.Name"));
    }

    [TestMethod]
    public void Get_InternsEveryParent()
    {
        var lava = GameplayTag.Get("GameplayTagTests.Damage.Fire.Lava");

        Assert.AreEqual(GameplayTag.Get("GameplayTagTests.Damage.Fire"), lava.Parent);
        Assert.AreEqual(GameplayTag.Get("GameplayTagTests.Damage"), lava.Parent.Parent);
        Assert.AreEqual(GameplayTag.Get("GameplayTagTests"), lava.Parent.Parent.Parent);
        Assert.IsTrue(lava.Parent.Parent.Parent.Parent.IsNone);
    }

    [TestMethod]
    public void NameSegmentAndDepth_DescribeTheTag()
    {
        var lava = GameplayTag.Get("GameplayTagTests.Describe.Lava");

        Assert.AreEqual("GameplayTagTests.Describe.Lava", lava.Name);
        Assert.AreEqual("Lava", lava.LastSegment);
        Assert.AreEqual(3, lava.Depth);
        Assert.AreEqual("GameplayTagTests.Describe.Lava", lava.ToString());
    }

    [TestMethod]
    public void Names_AreCaseSensitive()
    {
        Assert.AreNotEqual(GameplayTag.Get("GameplayTagTests.Case.fire"), GameplayTag.Get("GameplayTagTests.Case.Fire"));
    }

    [TestMethod]
    public void None_IsTheDefault_AndHasNoName()
    {
        Assert.IsTrue(default(GameplayTag).IsNone);
        Assert.AreEqual(GameplayTag.None, default);
        Assert.AreEqual("", GameplayTag.None.Name);
        Assert.AreEqual(0, GameplayTag.None.Depth);
        Assert.AreEqual("None", GameplayTag.None.ToString());
    }

    [TestMethod]
    public void IsSelfOrDescendantOf_MatchesItselfAndEveryAncestor_ButNotChildrenOrSiblings()
    {
        var damage = GameplayTag.Get("GameplayTagTests.Walk.Damage");
        var fire = GameplayTag.Get("GameplayTagTests.Walk.Damage.Fire");
        var lava = GameplayTag.Get("GameplayTagTests.Walk.Damage.Fire.Lava");
        var poison = GameplayTag.Get("GameplayTagTests.Walk.Damage.Poison");

        Assert.IsTrue(lava.IsSelfOrDescendantOf(lava));
        Assert.IsTrue(lava.IsSelfOrDescendantOf(fire));
        Assert.IsTrue(lava.IsSelfOrDescendantOf(damage));
        Assert.IsFalse(fire.IsSelfOrDescendantOf(lava));
        Assert.IsFalse(lava.IsSelfOrDescendantOf(poison));
        Assert.IsFalse(poison.IsSelfOrDescendantOf(fire));
    }

    [TestMethod]
    public void IsSelfOrDescendantOf_None_IsAlwaysFalse()
    {
        var fire = GameplayTag.Get("GameplayTagTests.NoneWalk.Fire");

        Assert.IsFalse(fire.IsSelfOrDescendantOf(GameplayTag.None));
        Assert.IsFalse(GameplayTag.None.IsSelfOrDescendantOf(fire));
        Assert.IsFalse(GameplayTag.None.IsSelfOrDescendantOf(GameplayTag.None));
    }

    [TestMethod]
    public void IsSelfOrDescendantOf_ATagWhoseNameMerelyStartsTheSame_IsNotAnAncestor()
    {
        var fire = GameplayTag.Get("GameplayTagTests.Prefix.Fire");
        var fireball = GameplayTag.Get("GameplayTagTests.Prefix.Fireball");

        Assert.IsFalse(fireball.IsSelfOrDescendantOf(fire));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(".")]
    [DataRow(".Fire")]
    [DataRow("Fire.")]
    [DataRow("Damage..Fire")]
    [DataRow("Damage.Fire Lava")]
    [DataRow("Damage-Fire")]
    public void Get_AnInvalidName_Throws(string name)
    {
        Assert.ThrowsExactly<ArgumentException>(() => GameplayTag.Get(name));
    }

    [TestMethod]
    public void Get_FromManyThreadsAtOnce_GivesOneTagPerName()
    {
        const int ThreadCount = 8;
        const int NameCount = 200;
        var tagsByThread = new GameplayTag[ThreadCount][];

        Parallel.For(0, ThreadCount, threadIndex =>
        {
            var tags = new GameplayTag[NameCount];
            for (var nameIndex = 0; nameIndex < NameCount; nameIndex++)
            {
                tags[nameIndex] = GameplayTag.Get($"GameplayTagTests.Concurrent.Group{nameIndex % 10}.Name{nameIndex}");
            }

            tagsByThread[threadIndex] = tags;
        });

        for (var threadIndex = 1; threadIndex < ThreadCount; threadIndex++)
        {
            CollectionAssert.AreEqual(tagsByThread[0], tagsByThread[threadIndex]);
        }

        Assert.HasCount(NameCount, tagsByThread[0].Distinct());
        for (var nameIndex = 0; nameIndex < NameCount; nameIndex++)
        {
            Assert.AreEqual($"GameplayTagTests.Concurrent.Group{nameIndex % 10}", tagsByThread[0][nameIndex].Parent.Name);
        }
    }
}
