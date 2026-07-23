using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class MathUtilityTests
{
    [TestMethod]
    public void ClampInt_ValueWithinRange_ReturnsValue()
    {
        Assert.AreEqual(5, MathUtility.ClampInt(5, 0, 10));
    }

    [TestMethod]
    public void ClampInt_ValueAboveMax_ReturnsMax()
    {
        Assert.AreEqual(10, MathUtility.ClampInt(15, 0, 10));
    }

    [TestMethod]
    public void ClampInt_ValueBelowMin_ReturnsMin()
    {
        Assert.AreEqual(0, MathUtility.ClampInt(-5, 0, 10));
    }

    [TestMethod]
    public void ClampShort_ValueAboveMax_ReturnsMax()
    {
        Assert.AreEqual((short)100, MathUtility.ClampShort(200, 0, 100));
    }

    [TestMethod]
    public void ClampInt_MinGreaterThanMax_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => MathUtility.ClampInt(5, 10, 0));
    }

    [TestMethod]
    public void ClampShort_MinGreaterThanMax_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => MathUtility.ClampShort(5, 10, 0));
    }

    [TestMethod]
    public void ClampByte_ValueWithinRange_ReturnsValue()
    {
        Assert.AreEqual((byte)100, MathUtility.ClampByte(100));
    }

    [TestMethod]
    public void ClampByte_ValueAboveMax_ReturnsMax()
    {
        Assert.AreEqual(byte.MaxValue, MathUtility.ClampByte(300));
    }

    [TestMethod]
    public void ClampByte_ValueBelowMin_ReturnsMin()
    {
        Assert.AreEqual(byte.MinValue, MathUtility.ClampByte(-5));
    }

    [TestMethod]
    public void RandomExceptFor_NeverReturnsASkippedValue()
    {
        var mathUtility = new MathUtility(new Random(12345));
        ReadOnlySpan<int> skip = [1, 3];

        for (var i = 0; i < 1000; i++)
        {
            var result = mathUtility.RandomExceptFor(5, skip);
            Assert.IsFalse(skip.Contains(result), $"Result {result} should have been excluded.");
            Assert.IsTrue(result is >= 0 and < 5);
        }
    }

    [TestMethod]
    public void RandomExceptFor_NoValuesToSkip_StaysInRange()
    {
        var mathUtility = new MathUtility(new Random(1));

        for (var i = 0; i < 100; i++)
        {
            var result = mathUtility.RandomExceptFor(3, []);
            Assert.IsTrue(result is >= 0 and < 3);
        }
    }

    [TestMethod]
    public void RandomExceptFor_UnsortedValuesToSkip_StillExcludesAll()
    {
        // The old rank-based algorithm required ascending order; deliberately pass
        // values out of order to confirm that's no longer a requirement.
        var mathUtility = new MathUtility(new Random(99));
        ReadOnlySpan<int> skip = [3, 0, 1];

        for (var i = 0; i < 1000; i++)
        {
            var result = mathUtility.RandomExceptFor(5, skip);
            Assert.IsFalse(skip.Contains(result), $"Result {result} should have been excluded.");
        }
    }

    [TestMethod]
    public void RandomExceptFor_DuplicateValuesToSkip_StillWorks()
    {
        var mathUtility = new MathUtility(new Random(7));
        ReadOnlySpan<int> skip = [1, 1, 1];

        for (var i = 0; i < 200; i++)
        {
            var result = mathUtility.RandomExceptFor(4, skip);
            Assert.AreNotEqual(1, result);
            Assert.IsTrue(result is >= 0 and < 4);
        }
    }

    [TestMethod]
    public void RandomExceptFor_RemainingValuesAreUniformlyDistributed()
    {
        // Regression test for the bias the old rank-based algorithm had toward values
        // immediately after a skipped one. With maximumValue=10 and one skipped value,
        // each of the 9 remaining values should be selected roughly equally often.
        var mathUtility = new MathUtility(new Random(2024));
        var counts = new int[10];
        const int trials = 90_000;

        for (var i = 0; i < trials; i++)
        {
            var result = mathUtility.RandomExceptFor(10, [4]);
            counts[result]++;
        }

        Assert.AreEqual(0, counts[4]);

        var expectedPerValue = trials / 9.0;
        for (var value = 0; value < counts.Length; value++)
        {
            if (value == 4)
            {
                continue;
            }

            var deviation = System.Math.Abs(counts[value] - expectedPerValue) / expectedPerValue;
            Assert.IsLessThan(0.1, deviation, $"Value {value} was selected {counts[value]} times, expected ~{expectedPerValue:F0} (deviation {deviation:P1}).");
        }
    }

    [TestMethod]
    public void RandomExceptFor_TooManyValuesToSkip_Throws()
    {
        var mathUtility = new MathUtility();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => mathUtility.RandomExceptFor(3, [0, 1, 2]));
    }
}