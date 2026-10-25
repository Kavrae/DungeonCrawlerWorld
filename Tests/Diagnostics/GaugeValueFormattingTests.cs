using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class GaugeValueFormattingTests
{
    [TestMethod]
    [DataRow("HeapBytes", 12.34 * 1024 * 1024, "12.3 MB")]
    [DataRow("GcPauseMilliseconds", 2.345, "2.35 ms")]
    [DataRow("Living", 660_123.0, "660,123")]
    [DataRow("Living", 0.5, "0.50")]
    public void AValue_IsFormattedByItsNamesUnit_AsTheF3WindowShowsIt(string gaugeName, double value, string expected)
    {
        Assert.AreEqual(expected, GaugeValueFormatting.Format(gaugeName, value));
    }

    [TestMethod]
    [DataRow("HeapBytes", GaugeUnit.Bytes)]
    [DataRow("UpdateMilliseconds", GaugeUnit.Milliseconds)]
    [DataRow("Gen0Collections", GaugeUnit.Count)]
    public void TheUnit_IsTheNamesSuffix(string gaugeName, GaugeUnit expected)
    {
        Assert.AreEqual(expected, GaugeUnits.Of(gaugeName));
    }

    [TestMethod]
    public void ACountersSummary_IsItsTotal_AndALevelsIsItsRange()
    {
        var statistics = GaugeWindowStatistics.Of([1, 4, 2, 0]);

        Assert.AreEqual("total 7", GaugeValueFormatting.FormatSummary(GaugeKind.Cumulative, GaugeUnit.Count, statistics));
        Assert.AreEqual("0 - 4", GaugeValueFormatting.FormatSummary(GaugeKind.Level, GaugeUnit.Count, statistics));
    }

    [TestMethod]
    public void WindowStatistics_AreMinimumMaximumTotalAndAverage()
    {
        var statistics = GaugeWindowStatistics.Of([3, -1, 4, 2]);

        Assert.AreEqual(new GaugeWindowStatistics(-1, 4, 8, 4), statistics);
        Assert.AreEqual(2, statistics.Average);
    }

    [TestMethod]
    public void AnEmptyWindow_IsAllZero()
    {
        Assert.AreEqual(default, GaugeWindowStatistics.Of([]));
        Assert.AreEqual(0, GaugeWindowStatistics.Of([]).Average);
    }
}
