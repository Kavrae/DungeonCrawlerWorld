using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class Vector3ByteTests
{
    [TestMethod]
    public void Equality_SameComponents_AreEqual()
    {
        var a = new Vector3Byte(1, 2, 3);
        var b = new Vector3Byte(1, 2, 3);

        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equality_DifferentComponents_AreNotEqual()
    {
        var a = new Vector3Byte(1, 2, 3);
        var b = new Vector3Byte(1, 2, 4);

        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void BroadcastConstructor_SetsAllComponentsToSameValue()
    {
        var value = new Vector3Byte(5);

        Assert.AreEqual(new Vector3Byte(5, 5, 5), value);
    }

    [TestMethod]
    public void DefaultConstructor_AllComponentsAreZero()
    {
        var value = new Vector3Byte();

        Assert.AreEqual(new Vector3Byte(0, 0, 0), value);
    }

    [TestMethod]
    public void Addition_SumsEachComponent()
    {
        var result = new Vector3Byte(1, 2, 3) + new Vector3Byte(10, 20, 30);

        Assert.AreEqual(new Vector3Byte(11, 22, 33), result);
    }

    [TestMethod]
    public void Subtraction_SubtractsEachComponent()
    {
        var result = new Vector3Byte(10, 20, 30) - new Vector3Byte(1, 2, 3);

        Assert.AreEqual(new Vector3Byte(9, 18, 27), result);
    }

    /// <summary>
    /// Regression test: the += operators used to cast the raw int sum straight to byte,
    /// silently wrapping past 255 instead of clamping -- e.g. 250 + 10 used to wrap to 4.
    /// </summary>
    [TestMethod]
    public void Addition_ExceedsByteMax_ClampsTo255InsteadOfWrapping()
    {
        var result = new Vector3Byte(250, 255, 200) + new Vector3Byte(10, 1, 100);

        Assert.AreEqual(new Vector3Byte(255, 255, 255), result);
    }

    /// <summary>
    /// Regression test: the -= operators used to cast the raw (possibly negative) int
    /// difference straight to byte, silently underflowing -- e.g. 0 - 1 used to wrap to 255.
    /// </summary>
    [TestMethod]
    public void Subtraction_Underflows_ClampsToZeroInsteadOfWrapping()
    {
        var result = new Vector3Byte(0, 1, 5) - new Vector3Byte(1, 5, 5);

        Assert.AreEqual(new Vector3Byte(0, 0, 0), result);
    }

    [TestMethod]
    public void ToString_ContainsAllComponents()
    {
        var value = new Vector3Byte(1, 2, 3);

        var text = value.ToString();

        StringAssert.Contains(text, "1");
        StringAssert.Contains(text, "2");
        StringAssert.Contains(text, "3");
    }
}
