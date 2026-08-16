using Engine.Math;

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
}
