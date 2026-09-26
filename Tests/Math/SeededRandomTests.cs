using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class SeededRandomTests
{
    private static int[] Draw(Random random, int count) => Enumerable.Range(0, count).Select(_ => random.Next(0, 1_000_000)).ToArray();

    [TestMethod]
    public void SameSeed_GivesTheSameSequence()
    {
        CollectionAssert.AreEqual(Draw(new SeededRandom(42), 50), Draw(new SeededRandom(42), 50));
    }

    [TestMethod]
    public void DifferentSeeds_GiveDifferentSequences()
    {
        CollectionAssert.AreNotEqual(Draw(new SeededRandom(42), 50), Draw(new SeededRandom(43), 50));
    }

    [TestMethod]
    public void Reseed_ReplaysTheSequenceFromTheStart()
    {
        var random = new SeededRandom(7);
        var first = Draw(random, 50);
        Draw(random, 13);

        random.Reseed(7);

        CollectionAssert.AreEqual(first, Draw(random, 50));
    }

    [TestMethod]
    public void NextInRange_StaysInsideTheRangeAndReachesBothEnds()
    {
        var random = new SeededRandom(3);
        var seen = new HashSet<int>();

        for (var i = 0; i < 2_000; i++)
        {
            var value = random.Next(-3, 4);
            Assert.IsTrue(value is >= -3 and < 4, $"{value} is outside [-3, 4)");
            seen.Add(value);
        }

        Assert.HasCount(7, seen);
    }

    [TestMethod]
    public void Next_EmptyRange_ReturnsTheMinimum()
    {
        var random = new SeededRandom(3);

        Assert.AreEqual(5, random.Next(5, 5));
        Assert.AreEqual(0, random.Next(0));
    }

    [TestMethod]
    public void Next_FullIntRange_DoesNotOverflow()
    {
        var random = new SeededRandom(9);

        for (var i = 0; i < 1_000; i++)
        {
            var value = random.Next(int.MinValue, int.MaxValue);
            Assert.AreNotEqual(int.MaxValue, value);
        }
    }

    [TestMethod]
    public void NextDouble_IsInTheUnitInterval()
    {
        var random = new SeededRandom(11);

        for (var i = 0; i < 1_000; i++)
        {
            var value = random.NextDouble();
            Assert.IsTrue(value is >= 0d and < 1d, $"{value} is outside [0, 1)");
        }
    }

    [TestMethod]
    public void NextBytes_FillsBuffersThatAreNotAMultipleOfEight()
    {
        var buffer = new byte[13];

        new SeededRandom(5).NextBytes(buffer);

        Assert.IsTrue(buffer[8..].Any(static value => value != 0));
    }

    [TestMethod]
    public void MathUtilityOverASeededRandom_ReplaysAfterReseed()
    {
        var random = new SeededRandom(21);
        var rolls = new MathUtility(random);
        var first = new[] { rolls.Next(0, 100), rolls.Next(0, 100), rolls.Next(0, 100) };

        random.Reseed(21);

        CollectionAssert.AreEqual(first, new[] { rolls.Next(0, 100), rolls.Next(0, 100), rolls.Next(0, 100) });
    }
}
