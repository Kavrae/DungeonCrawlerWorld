using Engine.Utilities;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using Presentation.UI.FloatingText;

namespace Tests.Presentation;

[TestClass]
public sealed class FloatingTextMotionTests
{
    private const float Tolerance = 0.0001f;

    [TestMethod]
    public void GetAlpha_FadesInOverTheRiseThenOutToNothing()
    {
        Assert.AreEqual(FloatingTextChrome.StartAlpha, FloatingTextMotion.GetAlpha(0), Tolerance);
        Assert.AreEqual(1f, FloatingTextMotion.GetAlpha(FloatingTextChrome.RiseFrames), Tolerance);
        Assert.AreEqual(0f, FloatingTextMotion.GetAlpha(FloatingTextChrome.LifetimeFrames), Tolerance);
    }

    [TestMethod]
    public void GetOffsetTiles_AtSpawn_IsZero() =>
        Assert.AreEqual(Vector2.Zero, FloatingTextMotion.GetOffsetTiles(0, horizontalDirection: 1, verticalDirection: -1));

    [TestMethod]
    public void GetOffsetTiles_EndOfRise_HasRisenOneTileAndDrifted()
    {
        var offset = FloatingTextMotion.GetOffsetTiles(FloatingTextChrome.RiseFrames, horizontalDirection: -1, verticalDirection: -1);

        Assert.AreEqual(-FloatingTextChrome.RiseDistanceTiles, offset.Y, Tolerance);
        Assert.AreEqual(-FloatingTextChrome.HorizontalDriftTilesPerSecond * FloatingTextChrome.RiseFrames / GameTiming.FramesPerSecond, offset.X, Tolerance);
    }

    [TestMethod]
    public void GetOffsetTiles_AfterRise_KeepsDrifting()
    {
        var endOfRise = FloatingTextMotion.GetOffsetTiles(FloatingTextChrome.RiseFrames, horizontalDirection: 1, verticalDirection: -1);
        var endOfLife = FloatingTextMotion.GetOffsetTiles(FloatingTextChrome.LifetimeFrames, horizontalDirection: 1, verticalDirection: -1);

        var driftFrames = FloatingTextChrome.LifetimeFrames - FloatingTextChrome.RiseFrames;
        Assert.AreEqual(-FloatingTextChrome.DriftTilesPerSecond * driftFrames / GameTiming.FramesPerSecond, endOfLife.Y - endOfRise.Y, Tolerance);
    }

    [TestMethod]
    public void GetOffsetTiles_Falling_MirrorsRising()
    {
        var rising = FloatingTextMotion.GetOffsetTiles(50, horizontalDirection: 1, verticalDirection: -1);
        var falling = FloatingTextMotion.GetOffsetTiles(50, horizontalDirection: 1, verticalDirection: 1);

        Assert.AreEqual(new Vector2(rising.X, -rising.Y), falling);
    }
}
