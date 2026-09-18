using Engine.Math;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class NeighborhoodsTests
{
    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1023, 0)]
    [DataRow(1024, 1)]
    [DataRow(3071, 2)]
    [DataRow(-1, -1)]
    [DataRow(-1024, -1)]
    [DataRow(-1025, -2)]
    public void CellOf_FloorsIncludingNegativeCoordinates(int tile, int expectedCell) =>
        Assert.AreEqual(expectedCell, Neighborhoods.CellOf(tile));

    [TestMethod]
    public void OriginOf_IsTheFirstTileOfTheCell()
    {
        Assert.AreEqual(1024, Neighborhoods.OriginOf(1));
        Assert.AreEqual(-1024, Neighborhoods.OriginOf(-1));
    }

    [TestMethod]
    public void Distance_IsChebyshevInNeighborhoodsAndIgnoresMapLayer()
    {
        var origin = new Vector3Int(1500, 1500, 0);

        Assert.AreEqual(0, Neighborhoods.Distance(origin, new Vector3Int(1024, 2047, 2)));
        Assert.AreEqual(1, Neighborhoods.Distance(origin, new Vector3Int(1023, 2048, 0)));
        Assert.AreEqual(2, Neighborhoods.Distance(origin, new Vector3Int(3072, 1500, 0)));
        Assert.AreEqual(2, Neighborhoods.Distance(origin, new Vector3Int(-1, 1500, 0)));
    }
}
