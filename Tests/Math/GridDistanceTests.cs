using Engine.Math;
using Engine.Tiles;

namespace Tests.Math;

[TestClass]
public sealed class GridDistanceTests
{
    [TestMethod]
    public void ChebyshevDistance_DiagonalCountsAsOneTile()
    {
        var a = new Vector3Int(5, 5, 0);
        var b = new Vector3Int(6, 6, 0);

        Assert.AreEqual(1, GridDistance.ChebyshevDistance(a, b));
    }

    [TestMethod]
    public void ChebyshevDistance_UsesTheLargerAxisDelta()
    {
        var a = new Vector3Int(0, 0, 0);
        var b = new Vector3Int(2, 5, 0);

        Assert.AreEqual(5, GridDistance.ChebyshevDistance(a, b));
    }

    [TestMethod]
    public void ChebyshevDistance_Footprints_MeasuresBetweenNearestTiles()
    {
        var largeOrigin = new Vector3Int(10, 10, 0);
        var largeSize = new Vector2Byte(3, 3);
        var single = new Vector2Byte(1, 1);

        Assert.AreEqual(1, GridDistance.ChebyshevDistance(largeOrigin, largeSize, new Vector3Int(13, 12, 0), single));
        Assert.AreEqual(1, GridDistance.ChebyshevDistance(new Vector3Int(9, 13, 0), single, largeOrigin, largeSize));
        Assert.AreEqual(0, GridDistance.ChebyshevDistance(largeOrigin, largeSize, new Vector3Int(12, 11, 0), single));
        Assert.AreEqual(2, GridDistance.ChebyshevDistance(largeOrigin, largeSize, new Vector3Int(14, 12, 0), single));
    }

    [TestMethod]
    public void ManhattanDistance_DiagonalCostsTwoTiles()
    {
        var a = new Vector3Int(5, 5, 0);
        var b = new Vector3Int(6, 6, 0);

        Assert.AreEqual(2, GridDistance.ManhattanDistance(a, b));
    }

    [TestMethod]
    public void ManhattanDistance_SumsBothAxisDeltas()
    {
        var a = new Vector3Int(0, 0, 0);
        var b = new Vector3Int(2, 5, 0);

        Assert.AreEqual(7, GridDistance.ManhattanDistance(a, b));
    }

    [TestMethod]
    [DataRow(1, 1, 1, true)]
    [DataRow(2, 0, 1, false)]
    [DataRow(2, 1, 2, true)]
    [DataRow(2, 2, 2, false)]
    [DataRow(3, 0, 3, true)]
    [DataRow(2, 2, 3, true)]
    [DataRow(3, 2, 3, false)]
    [DataRow(0, 0, 0, true)]
    [DataRow(0, 0, -1, false)]
    public void IsWithinRoundedRadius_HoldsTilesWhoseCentreIsWithinRadiusAndAHalf(int deltaX, int deltaY, int radius, bool expected)
    {
        Assert.AreEqual(expected, GridDistance.IsWithinRoundedRadius(deltaX, deltaY, radius));
    }

    [TestMethod]
    public void IsWithinRoundedRadius_Footprint_MeasuresFromTheNearestTile()
    {
        var origin = new Vector3Int(10, 10, 0);
        var size = new Vector2Byte(3, 2);

        Assert.IsTrue(GridDistance.IsWithinRoundedRadius(origin, size, new Vector3Int(11, 11, 0), radius: 0));
        Assert.IsTrue(GridDistance.IsWithinRoundedRadius(origin, size, new Vector3Int(14, 12, 0), radius: 2));
        Assert.IsFalse(GridDistance.IsWithinRoundedRadius(origin, size, new Vector3Int(14, 12, 0), radius: 1));
        Assert.IsFalse(GridDistance.IsWithinRoundedRadius(origin, size, new Vector3Int(7, 8, 0), radius: 2));
    }
}
