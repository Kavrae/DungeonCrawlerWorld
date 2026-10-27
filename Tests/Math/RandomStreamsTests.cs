using Engine.Math;

namespace Tests.Math;

[TestClass]
public sealed class RandomStreamsTests
{
    private static readonly Guid OwnerA = new("0b6e2d51-3f4a-4c7e-9a10-5d2c8e7f6a01");
    private static readonly Guid OwnerB = new("0b6e2d51-3f4a-4c7e-9a10-5d2c8e7f6a02");

    private static List<int> Draw(MathUtility stream, int count) => Enumerable.Range(0, count).Select(_ => stream.Next(0, 1_000_000)).ToList();

    [TestMethod]
    public void Get_SameKey_ReturnsTheSameStream()
    {
        var streams = new RandomStreams(1);

        Assert.AreSame(streams.Get(new RandomStreamKey(OwnerA, "Rolls")), streams.Get(new RandomStreamKey(OwnerA, "Rolls")));
    }

    [TestMethod]
    public void TheSameKeyAndSessionSeed_GiveTheSameSequence()
    {
        var key = new RandomStreamKey(OwnerA, "Rolls");

        CollectionAssert.AreEqual(Draw(new RandomStreams(3).Get(key), 20), Draw(new RandomStreams(3).Get(key), 20));
    }

    [TestMethod]
    public void AnotherOwner_Name_OrSessionSeed_GivesAnotherSequence()
    {
        var sequence = Draw(new RandomStreams(3).Get(new RandomStreamKey(OwnerA, "Rolls")), 20);

        CollectionAssert.AreNotEqual(sequence, Draw(new RandomStreams(3).Get(new RandomStreamKey(OwnerB, "Rolls")), 20));
        CollectionAssert.AreNotEqual(sequence, Draw(new RandomStreams(3).Get(new RandomStreamKey(OwnerA, "Other")), 20));
        CollectionAssert.AreNotEqual(sequence, Draw(new RandomStreams(4).Get(new RandomStreamKey(OwnerA, "Rolls")), 20));
    }

    [TestMethod]
    public void DrawingFromOneStream_DoesNotMoveAnother()
    {
        var key = new RandomStreamKey(OwnerA, "Rolls");
        var other = new RandomStreamKey(OwnerB, "Rolls");
        var untouched = new RandomStreams(3);
        var disturbed = new RandomStreams(3);

        Draw(disturbed.Get(other), 1_000);

        CollectionAssert.AreEqual(Draw(untouched.Get(key), 20), Draw(disturbed.Get(key), 20));
    }

    [TestMethod]
    public void SeedFor_IsTheSeedTheStreamStartsFrom()
    {
        var key = new RandomStreamKey(OwnerA, "Rolls");

        Assert.AreEqual(new RandomStreams(3).SeedFor(key), RandomStreams.SeedFor(3, key));
        CollectionAssert.AreEqual(Draw(new MathUtility(new SeededRandom(RandomStreams.SeedFor(3, key))), 20), Draw(new RandomStreams(3).Get(key), 20));
    }
}
