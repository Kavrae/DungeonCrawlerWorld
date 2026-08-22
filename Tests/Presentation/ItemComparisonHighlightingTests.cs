using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class ItemComparisonHighlightingTests
{
    [TestMethod]
    public void ComputeHighlight_AllValuesEqual_ReturnsNormal()
    {
        var result = ItemComparisonHighlighting.ComputeHighlight(5, [5, 5, 5], higherIsBetter: true);

        Assert.AreEqual(ComparisonHighlight.Normal, result);
    }

    [TestMethod]
    public void ComputeHighlight_HigherIsBetter_MaxValueReturnsBetter()
    {
        var result = ItemComparisonHighlighting.ComputeHighlight(10, [3, 10], higherIsBetter: true);

        Assert.AreEqual(ComparisonHighlight.Better, result);
    }

    [TestMethod]
    public void ComputeHighlight_HigherIsBetter_MinValueReturnsWorse()
    {
        var result = ItemComparisonHighlighting.ComputeHighlight(3, [3, 10], higherIsBetter: true);

        Assert.AreEqual(ComparisonHighlight.Worse, result);
    }

    [TestMethod]
    public void ComputeHighlight_LowerIsBetter_MinValueReturnsBetter()
    {
        var result = ItemComparisonHighlighting.ComputeHighlight(3, [3, 10], higherIsBetter: false);

        Assert.AreEqual(ComparisonHighlight.Better, result);
    }

    [TestMethod]
    public void ComputeHighlight_LowerIsBetter_MaxValueReturnsWorse()
    {
        var result = ItemComparisonHighlighting.ComputeHighlight(10, [3, 10], higherIsBetter: false);

        Assert.AreEqual(ComparisonHighlight.Worse, result);
    }

    [TestMethod]
    public void ComputeHighlight_ThreeValues_StrictlyMiddleValueReturnsNormal()
    {
        var result = ItemComparisonHighlighting.ComputeHighlight(2, [1, 2, 3], higherIsBetter: true);

        Assert.AreEqual(ComparisonHighlight.Normal, result);
    }
}
