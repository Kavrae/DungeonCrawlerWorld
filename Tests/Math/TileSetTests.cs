using Engine.Math;
using Engine.Tiles;

namespace Tests.Math;

[TestClass]
public sealed class TileSetTests
{
    private static Vector3Int Tile(int x, int y, int z = 0) => new(x, y, z);

    [TestMethod]
    public void SetTo_KeepsTheGivenOrderAndLeavesOutRepeats()
    {
        var tiles = new TileSet();

        tiles.SetTo([Tile(5, 2), Tile(-3, 7), Tile(5, 2), Tile(0, 0)]);

        CollectionAssert.AreEqual(new[] { Tile(5, 2), Tile(-3, 7), Tile(0, 0) }, tiles.ToArray());
        Assert.AreEqual(Tile(-3, 7), tiles[1]);
    }

    [TestMethod]
    public void Contains_IsTrueOnlyForItsOwnTilesOnItsOwnLayer()
    {
        var tiles = new TileSet();

        tiles.SetTo([Tile(10, 10, 1), Tile(12, 11, 1)]);

        Assert.IsTrue(tiles.Contains(Tile(12, 11, 1)));
        Assert.IsFalse(tiles.Contains(Tile(11, 10, 1)), "Inside the rectangle, not a tile of the set.");
        Assert.IsFalse(tiles.Contains(Tile(30, 30, 1)), "Outside the rectangle.");
        Assert.IsFalse(tiles.Contains(Tile(12, 11, 0)), "Same column and row, another layer.");
    }

    [TestMethod]
    public void SetTo_ReplacesWhatWasThere()
    {
        var tiles = new TileSet();
        tiles.SetTo([Tile(1, 1), Tile(2, 2)]);

        tiles.SetTo([Tile(40, 40)]);

        Assert.HasCount(1, tiles);
        Assert.IsFalse(tiles.Contains(Tile(1, 1)));
        Assert.IsTrue(tiles.Contains(Tile(40, 40)));
    }

    [TestMethod]
    public void Clear_LeavesNothing()
    {
        var tiles = new TileSet();
        tiles.SetTo([Tile(1, 1)]);

        tiles.Clear();

        Assert.IsEmpty(tiles);
        Assert.IsFalse(tiles.Contains(Tile(1, 1)));
    }

    [TestMethod]
    public void SetTo_TilesOnTwoLayers_Throws()
    {
        var tiles = new TileSet();

        Assert.ThrowsExactly<ArgumentException>(() => tiles.SetTo([Tile(1, 1, 0), Tile(2, 2, 1)]));
    }
}
