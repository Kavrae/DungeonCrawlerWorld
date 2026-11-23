using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class ItemComparisonHighlightingTests
{
    [TestMethod]
    [DataRow(5d, new[] { 5d, 5d, 5d }, true, ComparisonHighlight.Normal, DisplayName = "ComputeHighlight_AllValuesEqual_ReturnsNormal")]
    [DataRow(10d, new[] { 3d, 10d }, true, ComparisonHighlight.Better, DisplayName = "ComputeHighlight_HigherIsBetter_MaxValueReturnsBetter")]
    [DataRow(3d, new[] { 3d, 10d }, true, ComparisonHighlight.Worse, DisplayName = "ComputeHighlight_HigherIsBetter_MinValueReturnsWorse")]
    [DataRow(3d, new[] { 3d, 10d }, false, ComparisonHighlight.Better, DisplayName = "ComputeHighlight_LowerIsBetter_MinValueReturnsBetter")]
    [DataRow(10d, new[] { 3d, 10d }, false, ComparisonHighlight.Worse, DisplayName = "ComputeHighlight_LowerIsBetter_MaxValueReturnsWorse")]
    [DataRow(2d, new[] { 1d, 2d, 3d }, true, ComparisonHighlight.Normal, DisplayName = "ComputeHighlight_ThreeValues_StrictlyMiddleValueReturnsNormal")]
    public void ComputeHighlight_ReadsTheValueAgainstTheOthers(double value, double[] allValues, bool higherIsBetter, ComparisonHighlight expected)
    {
        var result = ItemComparisonHighlighting.ComputeHighlight(value, allValues, higherIsBetter);

        Assert.AreEqual(expected, result);
    }
}
