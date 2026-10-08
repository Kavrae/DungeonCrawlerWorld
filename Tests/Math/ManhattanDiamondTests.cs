using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class ManhattanDiamondTests
{
    private static readonly MapBounds Wide = new(0, 0, 1000, 1000, 2);

    private static Dictionary<Vector3Int, int> Visit(Vector3Int center, int radius, MapBounds bounds, Vector3Int? excludedCenter = null)
    {
        var visited = new Dictionary<Vector3Int, int>();
        ManhattanDiamond.ForEachCellOutside(center, radius, bounds, excludedCenter, visited, static (cellPosition, distance, cells) => cells.Add(cellPosition, distance));
        return visited;
    }

    [TestMethod]
    public void ForEachCell_VisitsTheDiamondOnceEachWithItsDistance()
    {
        var center = new Vector3Int(10, 10, 1);

        var visited = Visit(center, radius: 3, Wide);

        Assert.HasCount(25, visited, "2r(r+1)+1 cells for radius 3.");
        Assert.AreEqual(0, visited[center]);
        Assert.AreEqual(3, visited[new Vector3Int(12, 11, 1)], "Off-axis: 2 + 1.");
        Assert.IsFalse(visited.ContainsKey(new Vector3Int(12, 12, 1)), "Inside the same-radius square, outside the diamond.");
        Assert.IsTrue(visited.Keys.All(cell => cell.Z == 1), "Only the centre's layer.");
    }

    [TestMethod]
    public void ForEachCell_ClipsToBounds()
    {
        var visited = Visit(new Vector3Int(0, 0, 0), radius: 3, new MapBounds(0, 0, 2, 2, 1));

        CollectionAssert.AreEquivalent(new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0) }, visited.Keys.ToArray());
    }

    [TestMethod]
    public void ForEachCell_NegativeRadius_VisitsNothing()
    {
        Assert.IsEmpty(Visit(new Vector3Int(5, 5, 0), radius: -1, Wide));
    }

    [TestMethod]
    public void ForEachCellOutside_VisitsOnlyTheCellsTheMoveGained()
    {
        var from = new Vector3Int(20, 20, 0);
        var to = new Vector3Int(22, 21, 0);

        var gained = Visit(to, radius: 4, Wide, excludedCenter: from);

        var expected = Visit(to, radius: 4, Wide).Keys.Where(cell => System.Math.Abs(cell.X - from.X) + System.Math.Abs(cell.Y - from.Y) > 4).ToArray();
        CollectionAssert.AreEquivalent(expected, gained.Keys.ToArray());
        Assert.AreEqual(GridDistance.ManhattanDistance(to, new Vector3Int(26, 21, 0)), gained[new Vector3Int(26, 21, 0)], "Each cell still carries its distance from the centre it belongs to.");
    }

    [TestMethod]
    public void ForEachCellOutside_ExcludedCenterOnAnotherLayer_ExcludesNothing()
    {
        Assert.HasCount(25, Visit(new Vector3Int(20, 20, 0), radius: 3, Wide, excludedCenter: new Vector3Int(20, 20, 1)));
    }
}
