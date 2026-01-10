using DungeonCrawlerWorld.Utilities;

namespace UnitTests.Utility
{
    [TestClass]
    public sealed class StringUtilityTests
    {
        [TestMethod]
        [DataRow(0, 10, 5, "HP : [_____]", "Empty bar")]
        [DataRow(3, 10, 10, "HP : [===_______]", "Partially filled bar. Matching bar size and maximum value")]
        [DataRow(3, 10, 5, "HP : [=____]", "Partially filled bar. Bar smaller than maximum value")]
        [DataRow(3, 5, 10, "HP : [======____]", "Partially filled bar. Bar larger than maximum value")]
        [DataRow(7, 7, 14, "HP : [==============]", "Filled bar")]
        public void BuildPercentageBar_Tests(int currentValue, int maximumValue, int barSize, string expectedResults, string description)
        {
            var results = StringUtility.BuildPercentageBar(currentValue, maximumValue, barSize);
            Assert.AreEqual(expectedResults, results, description);
        }

        [TestMethod]
        [DataRow(50, 40, "Test", "Test", "String length less than maximum length.")]
        [DataRow(20, 40, "1234567890", "12345", "Simple percentage truncation")]
        [DataRow(20, 30, "1234567890", "123456", "Round down truncation")]
        [DataRow(20, 30, "", "", "Empty string")]
        public void FormatText_Truncate_Tests(int maximumPixelWidth, int textWidth, string originalText, string expectedResults, string description)
        {
            var textCriteria = new FormatTextCriteria
            {
                MaximumPixelWidth = maximumPixelWidth,
                TextWidth = textWidth,
                OriginalText = originalText,
                FormatTextMode = FormatTextMode.Truncate
            };

            var results = StringUtility.FormatText(textCriteria);
            Assert.AreEqual(expectedResults, results.FormattedText, description);
        }
    }
}
