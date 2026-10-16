using UIEngine;

namespace Tests.UIEngine;

[TestClass]
public sealed class KeyedCacheTests
{
    [TestMethod]
    public void Get_FirstCall_Builds()
    {
        var cache = new KeyedCache<int, string>();

        Assert.AreEqual("7G", cache.Get(7, static price => $"{price}G"));
    }

    [TestMethod]
    public void Get_SameKey_ReturnsKeptValueWithoutBuilding()
    {
        var cache = new KeyedCache<int, string>();
        var builds = 0;
        var first = cache.Get(7, price => { builds++; return $"{price}G"; });

        var second = cache.Get(7, price => { builds++; return $"{price}G"; });

        Assert.AreSame(first, second);
        Assert.AreEqual(1, builds);
    }

    [TestMethod]
    public void Get_DifferentKey_Rebuilds()
    {
        var cache = new KeyedCache<int, string>();
        cache.Get(7, static price => $"{price}G");

        Assert.AreEqual("8G", cache.Get(8, static price => $"{price}G"));
    }

    [TestMethod]
    public void Get_TupleKey_ComparesEveryElement()
    {
        var cache = new KeyedCache<(int Charges, int MaximumCharges), string>();
        cache.Get((3, 5), static key => $"{key.Charges}/{key.MaximumCharges}");

        Assert.AreEqual("3/6", cache.Get((3, 6), static key => $"{key.Charges}/{key.MaximumCharges}"));
    }

    [TestMethod]
    public void Get_WithState_PassesStateToBuild()
    {
        var cache = new KeyedCache<int, string>();

        Assert.AreEqual("Gold: 7", cache.Get(7, "Gold", static (amount, label) => $"{label}: {amount}"));
    }

    [TestMethod]
    public void Get_DefaultKeyOnFirstCall_StillBuilds()
    {
        var cache = new KeyedCache<int, string>();

        Assert.AreEqual("0G", cache.Get(0, static price => $"{price}G"));
    }

    [TestMethod]
    public void Clear_ThenSameKey_Rebuilds()
    {
        var cache = new KeyedCache<int, string>();
        var builds = 0;
        cache.Get(7, price => { builds++; return $"{price}G"; });

        cache.Clear();
        cache.Get(7, price => { builds++; return $"{price}G"; });

        Assert.AreEqual(2, builds);
    }
}
