using Engine.Utilities;

namespace Tests.Utilities;

[TestClass]
public sealed class GameTimingTests
{
    [TestMethod]
    public void FramesPerSecond_IsTheGamesRate() =>
        Assert.AreEqual(global::Game.GameFrameRate.FramesPerSecond, GameTiming.FramesPerSecond);

    [TestMethod]
    public void SetFramesPerSecond_SameRateAgain_DoesNothing()
    {
        GameTiming.SetFramesPerSecond(GameTiming.FramesPerSecond);

        Assert.AreEqual(global::Game.GameFrameRate.FramesPerSecond, GameTiming.FramesPerSecond);
    }

    [TestMethod]
    public void SetFramesPerSecond_DifferentRateOnceRead_Throws()
    {
        var readRate = GameTiming.FramesPerSecond;

        Assert.ThrowsExactly<InvalidOperationException>(() => GameTiming.SetFramesPerSecond((ushort)(readRate + 1)));
        Assert.AreEqual(readRate, GameTiming.FramesPerSecond);
    }
}
