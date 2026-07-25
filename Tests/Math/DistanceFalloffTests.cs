using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class DistanceFalloffTests
{
    /// <summary>The exact worked example from the feature spec: Strength 8 gives 8 stacks directly on the tile, 4 one tile away, 2 two tiles away, 1 three tiles away, 0 beyond that.</summary>
    [TestMethod]
    [DataRow(0, 8)]
    [DataRow(1, 4)]
    [DataRow(2, 2)]
    [DataRow(3, 1)]
    [DataRow(4, 0)]
    [DataRow(5, 0)]
    public void ValueAtDistance_StrengthEight_HalvesPerTile(int distance, int expected)
    {
        Assert.AreEqual(expected, DistanceFalloff.ValueAtDistance(8, distance));
    }

    [TestMethod]
    public void ValueAtDistance_NegativeDistance_ReturnsZero()
    {
        Assert.AreEqual(0, DistanceFalloff.ValueAtDistance(8, -1));
    }

    [TestMethod]
    public void MaxRadius_StrengthEight_ReturnsThree()
    {
        Assert.AreEqual(3, DistanceFalloff.MaxRadius(8));
    }

    [TestMethod]
    public void MaxRadius_NonPositiveStrength_ReturnsNegativeOne()
    {
        Assert.AreEqual(-1, DistanceFalloff.MaxRadius(0));
        Assert.AreEqual(-1, DistanceFalloff.MaxRadius(-5));
    }

    [TestMethod]
    public void ChebyshevDistance_DiagonalCountsAsOneTile()
    {
        var a = new Vector3Int(5, 5, 0);
        var b = new Vector3Int(6, 6, 0);

        Assert.AreEqual(1, DistanceFalloff.ChebyshevDistance(a, b));
    }

    [TestMethod]
    public void ChebyshevDistance_UsesTheLargerAxisDelta()
    {
        var a = new Vector3Int(0, 0, 0);
        var b = new Vector3Int(2, 5, 0);

        Assert.AreEqual(5, DistanceFalloff.ChebyshevDistance(a, b));
    }

    [TestMethod]
    public void ManhattanDistance_DiagonalCostsTwoTiles()
    {
        var a = new Vector3Int(5, 5, 0);
        var b = new Vector3Int(6, 6, 0);

        Assert.AreEqual(2, DistanceFalloff.ManhattanDistance(a, b));
    }

    [TestMethod]
    public void ManhattanDistance_SumsBothAxisDeltas()
    {
        var a = new Vector3Int(0, 0, 0);
        var b = new Vector3Int(2, 5, 0);

        Assert.AreEqual(7, DistanceFalloff.ManhattanDistance(a, b));
    }

    [TestMethod]
    public void ScatterManhattan_StrengthEight_VisitsExactlyTheDiamondWithCorrectContributions()
    {
        var visited = new Dictionary<Vector3Int, int>();
        var source = new Vector3Int(10, 10, 0);

        DistanceFalloff.ScatterManhattan(source, strength: 8, mapSize: new Vector3Int(1000, 1000, 1), visit: (cellPosition, contribution) =>
        {
            visited[cellPosition] = contribution;
        });

        // MaxRadius(8) == 3, so the diamond spans 25 cells (2*3^2 + 2*3 + 1); centre gets the full strength.
        Assert.HasCount(25, visited);
        Assert.AreEqual(8, visited[source]);
        Assert.AreEqual(4, visited[new Vector3Int(11, 10, 0)]);
        Assert.AreEqual(1, visited[new Vector3Int(13, 10, 0)]);
        Assert.AreEqual(1, visited[new Vector3Int(12, 11, 0)], "Manhattan distance 2+1=3 is within radius, even though it's off-axis -- this is the diamond shape, not a plus sign.");
        Assert.IsFalse(visited.ContainsKey(new Vector3Int(14, 10, 0)), "Distance 4 is beyond MaxRadius(8) == 3 and must not be visited at all.");
        Assert.IsFalse(visited.ContainsKey(new Vector3Int(13, 13, 0)), "Manhattan distance 3+3=6 is well outside the diamond, even though it's within a same-radius square (Chebyshev) -- confirms the shape is a diamond, not a square.");
    }

    [TestMethod]
    public void ScatterManhattan_ClampsToMapBounds()
    {
        var visitedCount = 0;
        var source = new Vector3Int(0, 0, 0);

        DistanceFalloff.ScatterManhattan(source, strength: 8, mapSize: new Vector3Int(2, 2, 1), visit: (_, _) => visitedCount++);

        // Only the 2x2 map's own cells can ever be visited, regardless of the strength-8 diamond's full extent.
        Assert.IsLessThanOrEqualTo(4, visitedCount);
    }

    [TestMethod]
    public void ScatterManhattan_NonPositiveStrength_VisitsNothing()
    {
        var visitedCount = 0;

        DistanceFalloff.ScatterManhattan(new Vector3Int(5, 5, 0), strength: 0, mapSize: new Vector3Int(1000, 1000, 1), visit: (_, _) => visitedCount++);

        Assert.AreEqual(0, visitedCount);
    }
}
