using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class UniqueNumberAllocatorTests
{
    [TestMethod]
    public void Allocate_ReturnsValueWithinDeclaredRange()
    {
        var allocator = new UniqueNumberAllocator(new MathUtility(new Random(1)), 1, 13_000_000);

        for (var i = 0; i < 1000; i++)
        {
            var value = allocator.Allocate();
            Assert.IsTrue(value is >= 1 and <= 13_000_000);
        }
    }

    [TestMethod]
    public void Allocate_NeverReturnsTheSameNumberTwice()
    {
        var allocator = new UniqueNumberAllocator(new MathUtility(new Random(1)), 1, 13_000_000);

        var seen = new HashSet<int>();
        for (var i = 0; i < 5000; i++)
        {
            Assert.IsTrue(seen.Add(allocator.Allocate()));
        }
    }

    [TestMethod]
    public void Constructor_MinValueGreaterThanMaxValue_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new UniqueNumberAllocator(new MathUtility(), 10, 5));
    }

    [TestMethod]
    public void Allocate_SingleValueRange_AlwaysReturnsThatValue()
    {
        var allocator = new UniqueNumberAllocator(new MathUtility(), 7, 7);

        Assert.AreEqual(7, allocator.Allocate());
    }
}
