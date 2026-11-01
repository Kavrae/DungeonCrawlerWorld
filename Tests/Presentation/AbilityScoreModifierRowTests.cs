using Presentation.UI.AbilityScores;
using Tests.UIEngine;

namespace Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class AbilityScoreModifierRowTests
{
    [TestMethod]
    public void ALineThatFits_IsDrawnUnchanged()
    {
        var measurer = TestFonts.Shared.GetTextMeasurer(TestFonts.Shared.GetFont(12));

        Assert.AreEqual("Ring : +1", AbilityScoreModifierRow.FitToWidth(measurer, "Ring : +1", "Ring", "+1", width: 500));
    }

    [TestMethod]
    public void ALineTooWideForItsColumn_ShortensTheSourceName_AndKeepsTheValue()
    {
        var measurer = TestFonts.Shared.GetTextMeasurer(TestFonts.Shared.GetFont(12));
        const string sourceName = "Gauntlets of the Exceedingly Long Name";
        var text = $"{sourceName} : +12%";
        var width = measurer.MeasureWidth(text) / 2;

        var fitted = AbilityScoreModifierRow.FitToWidth(measurer, text, sourceName, "+12%", width);

        StringAssert.EndsWith(fitted, " : +12%");
        Assert.AreNotEqual(text, fitted);
        Assert.IsLessThanOrEqualTo(width, measurer.MeasureWidth(fitted));
    }
}
