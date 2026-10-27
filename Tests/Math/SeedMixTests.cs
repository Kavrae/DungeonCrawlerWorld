using Engine.Math;

namespace Tests.Math;

/// <summary>Every derived seed in a seeded world rests on these values: a change here changes every world, so it must be a deliberate one.</summary>
[TestClass]
public sealed class SeedMixTests
{
    [TestMethod]
    public void Mix_GivesFixedValues()
    {
        Assert.AreEqual(0xE220A8397B1DCDAFUL, SeedMix.Mix(0, 0));
        Assert.AreEqual(0xF893A2EEFB32555EUL, SeedMix.Mix(1, 2));
    }

    [TestMethod]
    public void Mix_DependsOnTheOrderOfItsValues()
    {
        Assert.AreNotEqual(SeedMix.Mix(1, 2), SeedMix.Mix(2, 1));
    }

    [TestMethod]
    public void OfString_IsFnv1a()
    {
        Assert.AreEqual(0xD004BBAFA47097E5UL, SeedMix.Of("Effects"));
        Assert.AreEqual(0xCBF29CE484222325UL, SeedMix.Of(string.Empty));
    }

    [TestMethod]
    public void OfInt_DoesNotSignExtend()
    {
        Assert.AreEqual((ulong)uint.MaxValue, SeedMix.Of(-1));
    }

    [TestMethod]
    public void Pack_KeepsBothHalves_WithNegativesIncluded()
    {
        Assert.AreEqual(0xFFFFFFFF_00000002UL, SeedMix.Pack(-1, 2));
        Assert.AreNotEqual(SeedMix.Pack(1, 2), SeedMix.Pack(2, 1));
    }

    [TestMethod]
    public void OfGuid_IsStable_AndDiffersBetweenGuids()
    {
        var guid = new Guid("6f1c2a43-9a3e-4d8e-8b2a-0c5d7e9f1a2b");

        Assert.AreEqual(SeedMix.Of(guid), SeedMix.Of(new Guid("6f1c2a43-9a3e-4d8e-8b2a-0c5d7e9f1a2b")));
        Assert.AreNotEqual(SeedMix.Of(guid), SeedMix.Of(Guid.Empty));
    }
}
