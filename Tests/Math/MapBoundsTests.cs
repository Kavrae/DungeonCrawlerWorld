using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class MapBoundsTests
{
    private static readonly MapBounds Centred = new(-1024, -1024, 2048, 2048, 3);

    [TestMethod]
    [DataRow(-1024, -1024, 0, true)]
    [DataRow(-1, -1, 2, true)]
    [DataRow(2047, 2047, 1, true)]
    [DataRow(-1025, 0, 0, false)]
    [DataRow(0, 2048, 0, false)]
    [DataRow(0, 0, 3, false)]
    [DataRow(0, 0, -1, false)]
    [DataRow(int.MinValue / 2, int.MinValue / 2, 0, false)]
    [DataRow(int.MinValue, int.MaxValue, 0, false)]
    public void Contains_MinInclusiveMaxExclusive_OnEveryLayer(int x, int y, int z, bool expected) =>
        Assert.AreEqual(expected, Centred.Contains(new Vector3Int(x, y, z)));

    [TestMethod]
    public void FromSize_StartsAtZero()
    {
        var bounds = MapBounds.FromSize(new Vector3Int(30, 20, 2));

        Assert.AreEqual(new MapBounds(0, 0, 30, 20, 2), bounds);
        Assert.AreEqual(30, bounds.Width);
        Assert.AreEqual(20, bounds.Height);
    }
}
