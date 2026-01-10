using DungeonCrawlerWorld.Utilities;

namespace UnitTests.Utility
{
    [TestClass]
    public sealed class MathUtilityTests
    {
        [TestMethod]
        [DataRow(-1, 1, 2, 1, "Clamp from low value")]
        [DataRow(4, 1, 3, 3, "Clamp from high value")]
        [DataRow(2, 1, 3, 2, "Clamp not needed")]
        [DataRow(1, -2, -1, -1, "Clamp to negative value")]
        [DataRow(2, 1, 1, 1, "Min and max are equal")]
        public void ClampInt_Tests(int value, int min, int max, int expectedResults, string description)
        {
            var results = MathUtility.ClampInt(value, min, max);
            Assert.AreEqual(expectedResults, results, description);
        }

        [TestMethod]
        [DataRow((short)-1, (short)1, (short)2, (short)1, "Clamp from low value")]
        [DataRow((short)4, (short)1, (short)3, (short)3, "Clamp from high value")]
        [DataRow((short)2, (short)1, (short)3, (short)2, "Clamp not needed")]
        [DataRow((short)1, (short)-2, (short)-1, (short)-1, "Clamp to negative value")]
        [DataRow((short)2, (short)1, (short)1, (short)1, "Min and max are equal")]
        public void ClampShort_Tests(short value, short min, short max, short expectedResults, string description)
        {
            var results = MathUtility.ClampShort(value, min, max);
            Assert.AreEqual(expectedResults, results, description);
        }

        [TestMethod]
        public void RandomExceptFor_Tests()
        {
            var iterations = 20;
            var maximumValueExclusive = 6;
            var valuesToSkip = new int[] { 0, 2, 3, 5 };
            var valuesToAllow = new int[] { 1, 4 };

            for (var i = 0; i < iterations; i++)
            {
                var results = MathUtility.RandomExceptFor(maximumValueExclusive, valuesToSkip, valuesToSkip.Length);
                Assert.That(() => valuesToAllow.Contains(results), $"Iteration {i + 1}: Result {results} is not in allowed values.");
            }
        }
    }
}
