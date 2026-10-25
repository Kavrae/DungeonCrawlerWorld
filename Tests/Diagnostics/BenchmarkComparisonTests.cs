using Engine.Diagnostics;

namespace Tests.Diagnostics;

[TestClass]
public sealed class BenchmarkComparisonTests
{
    [TestMethod]
    public void EveryItemOfEitherRun_IsARow_AMissingOneReadingZero()
    {
        var rows = BenchmarkComparison.Compare(
            new Dictionary<string, double> { ["Update.SystemManager.A"] = 1.0, ["Update.SystemManager.New"] = 0.5 },
            new Dictionary<string, double> { ["Update.SystemManager.A"] = 1.0, ["Update.SystemManager.Gone"] = 0.3 });

        var byName = rows.ToDictionary(static row => row.Name);
        Assert.HasCount(3, rows);
        Assert.AreEqual(0, byName["Update.SystemManager.New"].Previous);
        Assert.IsNull(byName["Update.SystemManager.New"].ChangePercent);
        Assert.IsNull(byName["Update.SystemManager.New"].Ratio);
        Assert.AreEqual(0, byName["Update.SystemManager.Gone"].Current);
        Assert.AreEqual(-100.0, byName["Update.SystemManager.Gone"].ChangePercent);
    }

    [TestMethod]
    [DataRow(0.5, BenchmarkComparisonFlag.Regression)]
    [DataRow(0.01, BenchmarkComparisonFlag.None)]
    public void AnItemOnlyTheCurrentRunHas_IsARegression_OncePastTheMinimum(double current, BenchmarkComparisonFlag expected)
    {
        var row = BenchmarkComparison.Compare(new Dictionary<string, double> { ["X"] = current }, new Dictionary<string, double>()).Single();

        Assert.AreEqual(expected, row.Flag);
    }

    [TestMethod]
    public void TheChange_IsCurrentLessPrevious_ItsPercentRoundedToOneDecimal_AsTheSkillPrintsIt()
    {
        var row = BenchmarkComparison.Compare(new Dictionary<string, double> { ["X"] = 0.6033 }, new Dictionary<string, double> { ["X"] = 0.6262 }).Single();

        Assert.AreEqual(0.6033 - 0.6262, row.Change, 1e-12);
        Assert.AreEqual(-3.7, row.ChangePercent);
        Assert.AreEqual(0.6033 / 0.6262, row.Ratio!.Value, 1e-12);
    }

    [TestMethod]
    [DataRow(1.30, 1.00, BenchmarkComparisonFlag.Regression)]
    [DataRow(1.10, 1.00, BenchmarkComparisonFlag.None)]
    [DataRow(0.012, 0.01, BenchmarkComparisonFlag.None)]
    [DataRow(0.70, 1.00, BenchmarkComparisonFlag.Improvement)]
    [DataRow(0.008, 0.01, BenchmarkComparisonFlag.None)]
    public void ARow_IsFlagged_OnlyPastBothThePercentAndTheMinimum(double current, double previous, BenchmarkComparisonFlag expected)
    {
        var row = BenchmarkComparison.Compare(new Dictionary<string, double> { ["X"] = current }, new Dictionary<string, double> { ["X"] = previous }).Single();

        Assert.AreEqual(expected, row.Flag);
    }

    [TestMethod]
    public void Rows_AreLargestChangeFirst_EitherWay()
    {
        var rows = BenchmarkComparison.Compare(
            new Dictionary<string, double> { ["Small"] = 1.01, ["Fell"] = 0.5, ["Rose"] = 1.3 },
            new Dictionary<string, double> { ["Small"] = 1.0, ["Fell"] = 1.0, ["Rose"] = 1.0 });

        CollectionAssert.AreEqual(new[] { "Fell", "Rose", "Small" }, rows.Select(static row => row.Name).ToArray());
    }

    [TestMethod]
    public void TheThreshold_CanBeChosen()
    {
        var current = new Dictionary<string, double> { ["X"] = 1.12 };
        var previous = new Dictionary<string, double> { ["X"] = 1.0 };

        Assert.AreEqual(BenchmarkComparisonFlag.None, BenchmarkComparison.Compare(current, previous).Single().Flag);
        Assert.AreEqual(BenchmarkComparisonFlag.Regression, BenchmarkComparison.Compare(current, previous, thresholdPercent: 10).Single().Flag);
    }
}
