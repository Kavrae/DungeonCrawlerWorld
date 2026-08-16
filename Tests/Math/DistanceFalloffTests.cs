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
    public void ScatterManhattan_FadingShape_StrengthEight_VisitsExactlyTheDiamondWithCorrectContributions()
    {
        var visited = new Dictionary<Vector3Int, int>();
        var source = new Vector3Int(10, 10, 0);

        DistanceFalloff.ScatterManhattan(source, DistanceFalloff.MaxRadius(8), strength: 8, FalloffShape.Fading, mapSize: new Vector3Int(1000, 1000, 1), visit: (cellPosition, contribution) =>
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
    public void ScatterManhattan_FadingShape_RadiusSmallerThanMaxRadiusOfStrength_ClipsEarly()
    {
        var visited = new Dictionary<Vector3Int, int>();
        var source = new Vector3Int(10, 10, 0);

        // Strength 8's own MaxRadius is 3, but an explicit radius of 1 now clips the scatter to
        // just the centre and its four orthogonal neighbors -- radius is no longer derived from
        // strength, so a caller can bound it independently.
        DistanceFalloff.ScatterManhattan(source, radius: 1, strength: 8, FalloffShape.Fading, mapSize: new Vector3Int(1000, 1000, 1), visit: (cellPosition, contribution) =>
        {
            visited[cellPosition] = contribution;
        });

        Assert.HasCount(5, visited);
        Assert.AreEqual(8, visited[source]);
        Assert.AreEqual(4, visited[new Vector3Int(11, 10, 0)]);
        Assert.IsFalse(visited.ContainsKey(new Vector3Int(12, 10, 0)), "Distance 2 is within strength 8's own natural falloff radius (3), but must not be visited once radius is explicitly clipped to 1.");
    }

    [TestMethod]
    public void ScatterManhattan_FlatShape_VisitsEveryCellInRadiusAtFullStrength()
    {
        var visited = new Dictionary<Vector3Int, int>();
        var source = new Vector3Int(10, 10, 0);

        // A radius unrelated to strength's own falloff extent -- Flat has no notion of
        // MaxRadius(strength) at all, radius alone decides what's visited.
        DistanceFalloff.ScatterManhattan(source, radius: 2, strength: 8, FalloffShape.Flat, mapSize: new Vector3Int(1000, 1000, 1), visit: (cellPosition, contribution) =>
        {
            visited[cellPosition] = contribution;
        });

        // Radius 2 diamond: 2*2^2 + 2*2 + 1 = 13 cells.
        Assert.HasCount(13, visited);
        Assert.IsTrue(visited.Values.All(contribution => contribution == 8), "Every visited cell must get the full, undecayed strength under FalloffShape.Flat, regardless of distance from the source.");
    }

    [TestMethod]
    public void ScatterManhattan_ClampsToMapBounds()
    {
        var visitedCount = 0;
        var source = new Vector3Int(0, 0, 0);

        DistanceFalloff.ScatterManhattan(source, DistanceFalloff.MaxRadius(8), strength: 8, FalloffShape.Fading, mapSize: new Vector3Int(2, 2, 1), visit: (_, _) => visitedCount++);

        // Only the 2x2 map's own cells can ever be visited, regardless of the strength-8 diamond's full extent.
        Assert.IsLessThanOrEqualTo(4, visitedCount);
    }

    [TestMethod]
    public void ScatterManhattan_NegativeRadius_VisitsNothing()
    {
        var visitedCount = 0;

        // The real-world case this guards: a caller (e.g. AuraGrid.Splat) deriving radius from
        // MaxRadius(strength) for a non-positive strength gets -1 back, which must still mean
        // "nothing to scatter" now that radius, not strength, is what the loop bounds on.
        DistanceFalloff.ScatterManhattan(new Vector3Int(5, 5, 0), radius: DistanceFalloff.MaxRadius(0), strength: 0, FalloffShape.Fading, mapSize: new Vector3Int(1000, 1000, 1), visit: (_, _) => visitedCount++);

        Assert.AreEqual(0, visitedCount);
    }
}
