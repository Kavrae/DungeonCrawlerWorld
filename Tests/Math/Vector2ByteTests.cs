using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class Vector2ByteTests
{
    [TestMethod]
    public void Equality_SameComponents_AreEqual()
    {
        var a = new Vector2Byte(1, 2);
        var b = new Vector2Byte(1, 2);

        Assert.AreEqual(a, b);
        Assert.IsTrue(a == b);
        Assert.IsFalse(a != b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
    }

    [TestMethod]
    public void Equality_DifferentComponents_AreNotEqual()
    {
        var a = new Vector2Byte(1, 2);
        var b = new Vector2Byte(1, 3);

        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a != b);
    }

    [TestMethod]
    public void BroadcastConstructor_SetsAllComponentsToSameValue()
    {
        var value = new Vector2Byte(5);

        Assert.AreEqual(new Vector2Byte(5, 5), value);
    }

    [TestMethod]
    public void DefaultConstructor_AllComponentsAreZero()
    {
        var value = new Vector2Byte();

        Assert.AreEqual(new Vector2Byte(0, 0), value);
    }

    [TestMethod]
    public void Addition_SumsEachComponent()
    {
        var result = new Vector2Byte(1, 2) + new Vector2Byte(10, 20);

        Assert.AreEqual(new Vector2Byte(11, 22), result);
    }

    [TestMethod]
    public void Subtraction_SubtractsEachComponent()
    {
        var result = new Vector2Byte(10, 20) - new Vector2Byte(1, 2);

        Assert.AreEqual(new Vector2Byte(9, 18), result);
    }

    /// <summary>
    /// Regression test: the += operators used to cast the raw int sum straight to byte,
    /// silently wrapping past 255 instead of clamping -- e.g. 250 + 10 used to wrap to 4.
    /// </summary>
    [TestMethod]
    public void Addition_ExceedsByteMax_ClampsTo255InsteadOfWrapping()
    {
        var result = new Vector2Byte(250, 255) + new Vector2Byte(10, 1);

        Assert.AreEqual(new Vector2Byte(255, 255), result);
    }

    /// <summary>
    /// Regression test: the -= operators used to cast the raw (possibly negative) int
    /// difference straight to byte, silently underflowing -- e.g. 0 - 1 used to wrap to 255.
    /// </summary>
    [TestMethod]
    public void Subtraction_Underflows_ClampsToZeroInsteadOfWrapping()
    {
        var result = new Vector2Byte(0, 1) - new Vector2Byte(1, 5);

        Assert.AreEqual(new Vector2Byte(0, 0), result);
    }

    [TestMethod]
    public void ToString_ContainsAllComponents()
    {
        var value = new Vector2Byte(1, 2);

        var text = value.ToString();

        StringAssert.Contains(text, "1");
        StringAssert.Contains(text, "2");
    }
}
