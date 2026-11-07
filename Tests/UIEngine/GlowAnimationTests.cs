using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
public sealed class GlowAnimationTests
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(2);

    private static TimeSpan Periods(double count) => TimeSpan.FromTicks((long)(Period.Ticks * count));

    [TestMethod]
    public void Static_IsFullStrengthAtAnyTime()
    {
        var animation = GlowAnimation.Static;

        Assert.IsFalse(animation.IsPulsing);
        Assert.AreEqual(1f, animation.StrengthAt(TimeSpan.Zero));
        Assert.AreEqual(1f, animation.StrengthAt(TimeSpan.FromSeconds(1.234)));
        Assert.AreEqual(1f, animation.StrengthAt(TimeSpan.FromHours(3)));
    }

    [TestMethod]
    public void Default_IsStatic()
    {
        Assert.AreEqual(GlowAnimation.Static, default(GlowAnimation));
    }

    [TestMethod]
    public void Pulse_IsFullStrengthOnEveryPeriodBoundary()
    {
        var pulse = GlowAnimation.Pulse(Period);

        Assert.AreEqual(1f, pulse.StrengthAt(TimeSpan.Zero), 1e-5f);
        Assert.AreEqual(1f, pulse.StrengthAt(Periods(1)), 1e-5f);
        Assert.AreEqual(1f, pulse.StrengthAt(Periods(3)), 1e-5f);
    }

    [TestMethod]
    public void Pulse_HalfAPeriodIn_IsAtItsMinimum()
    {
        var pulse = GlowAnimation.Pulse(Period, minimumStrength: 0.25f);

        Assert.AreEqual(0.25f, pulse.StrengthAt(Periods(0.5)), 1e-5f);
        Assert.AreEqual(0.25f, pulse.StrengthAt(Periods(2.5)), 1e-5f);
    }

    [TestMethod]
    public void Pulse_AQuarterPeriodIn_IsBetweenItsMinimumAndFull()
    {
        var strength = GlowAnimation.Pulse(Period, minimumStrength: 0.25f).StrengthAt(Periods(0.25));

        Assert.IsGreaterThan(0.25f, strength);
        Assert.IsLessThan(1f, strength);
    }

    [TestMethod]
    public void Pulse_StaysWithinItsMinimumAndFull()
    {
        var pulse = GlowAnimation.Pulse(Period, minimumStrength: 0.25f, cycles: null);

        for (var step = 0; step < 400; step++)
        {
            var strength = pulse.StrengthAt(TimeSpan.FromMilliseconds(step * 37));
            Assert.IsGreaterThanOrEqualTo(0.25f - 1e-5f, strength);
            Assert.IsLessThanOrEqualTo(1f + 1e-5f, strength);
        }
    }

    [TestMethod]
    public void Pulse_DefaultsToFiveCyclesAndAQuarterMinimum()
    {
        var pulse = GlowAnimation.Pulse(Period);

        Assert.AreEqual(5, pulse.PulseCycles);
        Assert.AreEqual(GlowAnimation.DefaultPulseCycles, pulse.PulseCycles);
        Assert.AreEqual(0.25f, pulse.PulseMinimumStrength);
        Assert.IsFalse(pulse.PulsesForever);
    }

    [TestMethod]
    public void Pulse_WithFiveCycles_PulsesThroughTheFifthThenHoldsFullStrength()
    {
        var pulse = GlowAnimation.Pulse(Period, cycles: 5);

        Assert.AreEqual(0.25f, pulse.StrengthAt(Periods(4.5)), 1e-5f);
        Assert.AreEqual(1f, pulse.StrengthAt(Periods(5)));
        Assert.AreEqual(1f, pulse.StrengthAt(Periods(5.5)));
        Assert.AreEqual(1f, pulse.StrengthAt(TimeSpan.FromHours(1)));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(int.MaxValue)]
    public void Pulse_WithNoCycleCount_PulsesForever(int? cycles)
    {
        var pulse = GlowAnimation.Pulse(Period, cycles: cycles);

        Assert.IsTrue(pulse.PulsesForever);
        Assert.IsNull(pulse.PulseCycles);
        Assert.AreEqual(0.25f, pulse.StrengthAt(Periods(1000.5)), 1e-5f);
    }

    [TestMethod]
    public void Pulse_HoursIn_KeepsItsPrecision()
    {
        var pulse = GlowAnimation.Pulse(Period, cycles: null);

        Assert.AreEqual(pulse.StrengthAt(Periods(0.25)), pulse.StrengthAt(TimeSpan.FromHours(10) + Periods(0.25)), 1e-4f);
    }

    [TestMethod]
    public void Pulse_FasterThanThreeCyclesASecond_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GlowAnimation.Pulse(TimeSpan.FromSeconds(0.3)));
    }

    [TestMethod]
    public void Pulse_AtThreeCyclesASecond_IsAllowed()
    {
        Assert.IsTrue(GlowAnimation.Pulse(GlowAnimation.MinimumPulsePeriod).IsPulsing);
    }

    [TestMethod]
    [DataRow(-0.1f)]
    [DataRow(1.1f)]
    public void Pulse_WithAMinimumOutsideZeroToOne_Throws(float minimumStrength)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GlowAnimation.Pulse(Period, minimumStrength));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-3)]
    public void Pulse_WithFewerThanOneCycle_Throws(int cycles)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => GlowAnimation.Pulse(Period, cycles: cycles));
    }

    [TestMethod]
    public void Pulse_WithANaNMinimum_TakesItAsZero()
    {
        var pulse = GlowAnimation.Pulse(Period, float.NaN);

        Assert.AreEqual(0f, pulse.PulseMinimumStrength);
        Assert.AreEqual(0f, pulse.StrengthAt(Periods(0.5)), 1e-5f);
    }
}
