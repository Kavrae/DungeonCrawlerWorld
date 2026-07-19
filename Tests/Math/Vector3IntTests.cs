using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class Vector3IntTests
{
    [TestMethod]
    public void Equality_SameComponents_AreEqual()
    {
        var a = new Vector3Int(1, 2, 3);
        var b = new Vector3Int(1, 2, 3);

        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equality_DifferentComponents_AreNotEqual()
    {
        var a = new Vector3Int(1, 2, 3);
        var b = new Vector3Int(1, 2, 4);

        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void Addition_SumsEachComponent()
    {
        var result = new Vector3Int(1, 2, 3) + new Vector3Int(10, 20, 30);

        Assert.AreEqual(new Vector3Int(11, 22, 33), result);
    }

    [TestMethod]
    public void Subtraction_SubtractsEachComponent()
    {
        var result = new Vector3Int(10, 20, 30) - new Vector3Int(1, 2, 3);

        Assert.AreEqual(new Vector3Int(9, 18, 27), result);
    }

    [TestMethod]
    public void BroadcastConstructor_SetsAllComponentsToSameValue()
    {
        var value = new Vector3Int(5);

        Assert.AreEqual(new Vector3Int(5, 5, 5), value);
    }

    [TestMethod]
    public void CubeInt_PositionOnlyConstructor_DefaultsSizeToOne()
    {
        var cube = new CubeInt(new Vector3Int(1, 2, 3));

        Assert.AreEqual(new Vector3Int(1, 1, 1), cube.Size);
    }

    [TestMethod]
    public void CubeInt_Equality_SamePositionAndSize_AreEqual()
    {
        var a = new CubeInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
        var b = new CubeInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));

        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void CubeInt_Equality_DifferentSize_AreNotEqual()
    {
        var a = new CubeInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 6));
        var b = new CubeInt(new Vector3Int(1, 2, 3), new Vector3Int(4, 5, 7));

        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a != b);
    }
}
