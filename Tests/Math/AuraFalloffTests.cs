using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class AuraFalloffTests
{
    [TestMethod]
    [DataRow(0, 16)]
    [DataRow(1, 13)]
    [DataRow(2, 10)]
    [DataRow(3, 7)]
    [DataRow(4, 4)]
    [DataRow(5, 0)]
    public void Linear_PowerSixteenSizeFour_FallsInEqualStepsRoundedUp(int distance, int expected)
    {
        Assert.AreEqual(expected, AuraFalloff.Linear.ValueAt(power: 16, size: 4, distance));
    }

    [TestMethod]
    public void Linear_EveryPowerAndSizeUpTo255_IsAtLeastOneAtTheEdgeAndZeroPastIt()
    {
        for (var power = 1; power <= byte.MaxValue; power++)
        {
            for (var size = 0; size <= byte.MaxValue; size++)
            {
                Assert.AreEqual(power, AuraFalloff.Linear.ValueAt(power, size, 0), $"power {power} size {size} at the source");
                Assert.IsGreaterThanOrEqualTo(1, AuraFalloff.Linear.ValueAt(power, size, size), $"power {power} size {size} at the edge");
                Assert.AreEqual(0, AuraFalloff.Linear.ValueAt(power, size, size + 1), $"power {power} size {size} past the edge");
            }
        }
    }

    [TestMethod]
    public void Linear_NeverRisesWithDistance()
    {
        foreach (var (power, size) in new[] { (1, 30), (7, 3), (16, 4), (65535, 255) })
        {
            for (var distance = 1; distance <= size; distance++)
            {
                Assert.IsLessThanOrEqualTo(AuraFalloff.Linear.ValueAt(power, size, distance - 1), AuraFalloff.Linear.ValueAt(power, size, distance), $"power {power} size {size} at {distance}");
            }
        }
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(15)]
    [DataRow(30)]
    public void None_IsFullPowerToTheEdge(int distance)
    {
        Assert.AreEqual(9, AuraFalloff.None.ValueAt(power: 9, size: 30, distance));
    }

    [TestMethod]
    public void EitherFalloff_PastTheSizeOrAtANegativeDistance_IsZero()
    {
        foreach (var falloff in new[] { AuraFalloff.Linear, AuraFalloff.None })
        {
            Assert.AreEqual(0, falloff.ValueAt(power: 9, size: 30, 31));
            Assert.AreEqual(0, falloff.ValueAt(power: 9, size: 30, -1));
            Assert.AreEqual(0, falloff.ValueAt(power: 0, size: 30, 0));
        }
    }

    [TestMethod]
    public void Linear_LargestPowerAndSize_DoesNotOverflow()
    {
        Assert.AreEqual(65535, AuraFalloff.Linear.ValueAt(power: 65535, size: 255, 0));
        Assert.AreEqual(256, AuraFalloff.Linear.ValueAt(power: 65535, size: 255, 255));
    }
}
