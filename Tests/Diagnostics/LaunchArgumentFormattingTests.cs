using Engine.Diagnostics;
using Engine.Math;

namespace Tests.Diagnostics;

[TestClass]
public sealed class LaunchArgumentFormattingTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(12345)]
    [DataRow(-7)]
    [DataRow(int.MinValue)]
    [DataRow(int.MaxValue)]
    public void ASeedFormatted_ParsesBackAsTheSameSeed(int seed)
    {
        Assert.AreEqual(seed, RandomSeed.Parse(["--headless", RandomSeed.FormatArgument(seed)]));
    }

    [TestMethod]
    public void ASeed_FormatsAsSeedEqualsTheNumber()
    {
        Assert.AreEqual("--seed=12345", RandomSeed.FormatArgument(12345));
    }

    [TestMethod]
    [DataRow(0L, 1L)]
    [DataRow(600L, 3600L)]
    [DataRow(1_000_000L, 9_000_000_000L)]
    public void ARangeFormatted_ParsesBackAsTheSameRange(long startFrame, long endFrame)
    {
        var range = new BenchmarkFrameRange(startFrame, endFrame);

        Assert.AreEqual(range, BenchmarkFrameRange.Parse([range.FormatArgument()]));
    }

    [TestMethod]
    public void ARange_FormatsAsStartDashEnd()
    {
        Assert.AreEqual("--benchmark-frames=600-3600", new BenchmarkFrameRange(600, 3600).FormatArgument());
    }

    [TestMethod]
    public void Headless_IsRefusedOnlyWithoutARange()
    {
        Assert.IsTrue(HeadlessArgument.IsRefused(["--headless"]));
        Assert.IsTrue(HeadlessArgument.IsRefused(["--HEADLESS", "--benchmark-frames=50-10"]));
        Assert.IsFalse(HeadlessArgument.IsRefused(["--headless", "--benchmark-frames=600-3600"]));
        Assert.IsFalse(HeadlessArgument.IsRefused(["--benchmark-frames=600-3600"]));
        Assert.IsFalse(HeadlessArgument.IsRefused([]));
    }
}
