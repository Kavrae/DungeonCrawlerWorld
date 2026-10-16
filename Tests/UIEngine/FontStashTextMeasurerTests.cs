namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class FontStashTextMeasurerTests
{
    [TestMethod]
    public void MeasureWidth_AWholeString_MatchesTheFontsOwnMeasurement()
    {
        var font = TestFonts.Shared.GetFont(16);
        var measurer = TestFonts.Shared.GetTextMeasurer(font);

        Assert.AreEqual(font.MeasureString("Health Potion").X, measurer.MeasureWidth("Health Potion"));
    }

    [TestMethod]
    public void MeasureWidth_PartOfAString_MatchesMeasuringThatPartAsItsOwnString()
    {
        var font = TestFonts.Shared.GetFont(16);
        var measurer = TestFonts.Shared.GetTextMeasurer(font);

        Assert.AreEqual(font.MeasureString("Potion").X, measurer.MeasureWidth("Health Potion".AsSpan(7)));
    }

    [TestMethod]
    public void GetTextMeasurer_TheSameFontTwice_IsTheSameMeasurer()
    {
        var font = TestFonts.Shared.GetFont(16);

        Assert.AreSame(TestFonts.Shared.GetTextMeasurer(font), TestFonts.Shared.GetTextMeasurer(font));
    }
}
