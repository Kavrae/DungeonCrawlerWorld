using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class UniqueNumberAllocatorTests
{
    private static List<int> Drain(UniqueNumberAllocator allocator)
    {
        var numbers = new List<int>();
        while (allocator.TryAllocate(out var number))
        {
            numbers.Add(number);
        }

        return numbers;
    }

    [TestMethod]
    public void TryAllocate_ReturnsValuesWithinDeclaredRange()
    {
        var allocator = new UniqueNumberAllocator(1, 1, 24);

        for (var i = 0; i < 1000; i++)
        {
            Assert.IsTrue(allocator.TryAllocate(out var value));
            Assert.IsTrue(value is >= 1 and <= 16_777_216);
        }
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(10)]
    public void TryAllocate_UntilExhausted_ReturnsEveryValueInTheRangeExactlyOnce(int valueBits)
    {
        var allocator = new UniqueNumberAllocator(3, -5, valueBits);

        var numbers = Drain(allocator);

        CollectionAssert.AreEquivalent(Enumerable.Range(-5, 1 << valueBits).ToList(), numbers);
    }

    [TestMethod]
    public void TryAllocate_OnceExhausted_KeepsReturningFalse()
    {
        var allocator = new UniqueNumberAllocator(1, 1, 2);
        for (var i = 0; i < 4; i++)
        {
            Assert.IsFalse(allocator.IsExhausted);
            Assert.IsTrue(allocator.TryAllocate(out _));
        }

        Assert.IsTrue(allocator.IsExhausted);
        Assert.IsFalse(allocator.TryAllocate(out var number));
        Assert.AreEqual(0, number);
        Assert.IsFalse(allocator.TryAllocate(out _));
    }

    [TestMethod]
    public void TheSameSeed_GivesTheSameSequence_AndADifferentSeedADifferentOne()
    {
        var first = Drain(new UniqueNumberAllocator(42, 1, 10));
        var second = Drain(new UniqueNumberAllocator(42, 1, 10));
        var other = Drain(new UniqueNumberAllocator(43, 1, 10));

        CollectionAssert.AreEqual(first, second);
        CollectionAssert.AreNotEqual(first, other);
    }

    [TestMethod]
    public void TryAllocate_IsShuffled_NotTheCounter()
    {
        var allocator = new UniqueNumberAllocator(1, 1, 24);
        var numbers = new List<int>();
        for (var i = 0; i < 1000; i++)
        {
            allocator.TryAllocate(out var number);
            numbers.Add(number);
        }

        Assert.IsTrue(numbers.Zip(numbers.Skip(1)).Any(static pair => pair.First > pair.Second));
        Assert.IsLessThan(16_777_216 / 4, numbers.Min());
        Assert.IsGreaterThan(16_777_216 / 4 * 3, numbers.Max());
    }

    [TestMethod]
    public void TryAllocate_AtTheTopOfTheIntRange_DoesNotOverflow()
    {
        var allocator = new UniqueNumberAllocator(1, int.MaxValue - 3, 2);

        CollectionAssert.AreEquivalent(new[] { int.MaxValue - 3, int.MaxValue - 2, int.MaxValue - 1, int.MaxValue }, Drain(allocator));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(3)]
    [DataRow(32)]
    public void Constructor_OddOrOutOfRangeValueBits_Throws(int valueBits)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new UniqueNumberAllocator(1, 1, valueBits));
    }

    [TestMethod]
    public void Constructor_RangePastIntMaxValue_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new UniqueNumberAllocator(1, int.MaxValue - 2, 2));
    }
}
