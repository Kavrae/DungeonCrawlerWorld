using Engine.Math;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class NeighborhoodBitsTests
{
    [TestMethod]
    public void IsSet_NothingSet_IsFalseEverywhere()
    {
        var bits = new NeighborhoodBits(depth: 3);

        Assert.IsFalse(bits.IsSet(new Vector3Int(0, 0, 0)));
        Assert.IsFalse(bits.IsSet(new Vector3Int(-5000, 7000, 2)));
    }

    [TestMethod]
    public void Set_MarksOnlyThatCell()
    {
        var bits = new NeighborhoodBits(depth: 3);
        var cell = new Vector3Int(1023, 5, 1);

        bits.Set(cell);

        Assert.IsTrue(bits.IsSet(cell));
        Assert.IsFalse(bits.IsSet(new Vector3Int(1022, 5, 1)));
        Assert.IsFalse(bits.IsSet(new Vector3Int(1023, 6, 1)));
        Assert.IsFalse(bits.IsSet(new Vector3Int(1023, 5, 0)), "The same cell on another layer is its own bit.");
        Assert.IsFalse(bits.IsSet(new Vector3Int(1024, 5, 1)), "The next cell east is in the next neighborhood.");
    }

    [TestMethod]
    public void Set_InNegativeAndFarNeighborhoods_KeepsThemApart()
    {
        var bits = new NeighborhoodBits(depth: 3);
        var west = new Vector3Int(-1, -1, 2);
        var east = new Vector3Int(1023, 1023, 2);

        bits.Set(west);

        Assert.IsTrue(bits.IsSet(west));
        Assert.IsFalse(bits.IsSet(east), "Both are the last cell of their own neighborhood.");
    }

    [TestMethod]
    public void Clear_UnsetsTheCellAndLeavesOthers()
    {
        var bits = new NeighborhoodBits(depth: 3);
        var first = new Vector3Int(10, 10, 0);
        var second = new Vector3Int(11, 10, 0);
        bits.Set(first);
        bits.Set(second);

        bits.Clear(first);

        Assert.IsFalse(bits.IsSet(first));
        Assert.IsTrue(bits.IsSet(second));
    }

    /// <summary>A neighborhood's bitmap goes when its last bit clears; setting a bit there afterwards starts a fresh one.</summary>
    [TestMethod]
    public void Clear_LastBitOfANeighborhood_ThenSetAgain_Works()
    {
        var bits = new NeighborhoodBits(depth: 3);
        var cell = new Vector3Int(2000, 2000, 1);
        bits.Set(cell);

        bits.Clear(cell);
        Assert.IsFalse(bits.IsSet(cell));

        bits.Set(cell);
        Assert.IsTrue(bits.IsSet(cell));
    }

    [TestMethod]
    public void SetTwice_ThenClearOnce_IsUnset()
    {
        var bits = new NeighborhoodBits(depth: 3);
        var cell = new Vector3Int(3, 4, 0);
        bits.Set(cell);
        bits.Set(cell);

        bits.Clear(cell);

        Assert.IsFalse(bits.IsSet(cell));
    }
}
