using Microsoft.Xna.Framework;
using Presentation.MapRendering;
using UIEngine.Rendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class MapOverlayTexelsTests
{
    private static readonly Point Scroll = new(20, 30);

    private static MapOverlayTexels Begun(int columns = 10, int rows = 6)
    {
        var texels = new MapOverlayTexels();
        texels.Begin(Scroll, columns, rows);
        return texels;
    }

    private static Color Wash(MapOverlayTexels texels, int column, int row) => texels.TexelAt(column, row, SpriteEffectTileOverlay.WashTexel);

    private static Color Fill(MapOverlayTexels texels, int column, int row, int slot) => texels.TexelAt(column, row, SpriteEffectTileOverlay.FirstFillTexel + slot);

    private static Color Fractions(MapOverlayTexels texels, int column, int row) => texels.TexelAt(column, row, SpriteEffectTileOverlay.FractionsTexel);

    [TestMethod]
    public void Covers_TexelsPerTileForEveryColumn_AndARowPerRow()
    {
        var texels = Begun(10, 6);

        Assert.AreEqual(10 * SpriteEffectTileOverlay.TexelsPerTile, texels.Width);
        Assert.AreEqual(6, texels.Height);
        Assert.IsTrue(texels.IsEmpty);
    }

    [TestMethod]
    public void AWash_IsItsColour_OnItsTileOnly()
    {
        var texels = Begun();

        texels.AddWash(Scroll.X + 3, Scroll.Y + 2, Color.LightGreen * 0.5f);

        Assert.AreEqual(Color.LightGreen * 0.5f, Wash(texels, 3, 2));
        Assert.AreEqual(Color.Transparent, Wash(texels, 4, 2));
        Assert.IsFalse(texels.IsEmpty);
    }

    [TestMethod]
    public void TwoWashes_CompositeInTheOrderAdded()
    {
        var texels = Begun();
        var under = Color.Red * 0.5f;
        var over = Color.Blue * 0.5f;

        texels.AddWash(Scroll.X, Scroll.Y, under);
        texels.AddWash(Scroll.X, Scroll.Y, over);

        Assert.AreEqual(new Color(over.ToVector4() + under.ToVector4() * (1f - over.A / 255f)), Wash(texels, 0, 0));
    }

    [TestMethod]
    public void AFill_IsItsColourAndFraction()
    {
        var texels = Begun();

        texels.AddFill(Scroll.X + 1, Scroll.Y + 1, Color.Red * 0.5f, 0.4f);

        Assert.AreEqual(Color.Red * 0.5f, Fill(texels, 1, 1, 0));
        Assert.AreEqual(SpriteEffectTileOverlay.FractionByte(0.4f), Fractions(texels, 1, 1).R);
        Assert.AreEqual(Color.Transparent, Fill(texels, 1, 1, 1));
    }

    [TestMethod]
    public void TwoFills_TheLargerFirst_SoTheSmallerShowsOverIt()
    {
        var texels = Begun();

        texels.AddFill(Scroll.X, Scroll.Y, Color.Red, 0.25f);
        texels.AddFill(Scroll.X, Scroll.Y, Color.Yellow, 0.75f);

        Assert.AreEqual(Color.Yellow, Fill(texels, 0, 0, 0));
        Assert.AreEqual(Color.Red, Fill(texels, 0, 0, 1));
        Assert.AreEqual(SpriteEffectTileOverlay.FractionByte(0.75f), Fractions(texels, 0, 0).R);
        Assert.AreEqual(SpriteEffectTileOverlay.FractionByte(0.25f), Fractions(texels, 0, 0).G);
    }

    [TestMethod]
    public void AThirdFill_KeepsTheTwoNearestToResolving()
    {
        var texels = Begun();

        texels.AddFill(Scroll.X, Scroll.Y, Color.Red, 0.5f);
        texels.AddFill(Scroll.X, Scroll.Y, Color.Yellow, 0.1f);
        texels.AddFill(Scroll.X, Scroll.Y, Color.Green, 0.3f);

        Assert.AreEqual(Color.Red, Fill(texels, 0, 0, 0));
        Assert.AreEqual(Color.Green, Fill(texels, 0, 0, 1));
        Assert.AreEqual(SpriteEffectTileOverlay.FractionByte(0.3f), Fractions(texels, 0, 0).G);
    }

    [TestMethod]
    public void ARing_IsItsColour_WithItsDepthInTheFractions()
    {
        var texels = Begun();

        texels.AddFill(Scroll.X + 2, Scroll.Y, Color.Red, 0.5f);
        texels.SetRing(Scroll.X + 2, Scroll.Y, Color.LightBlue * 0.5f, 5);

        Assert.AreEqual(Color.LightBlue * 0.5f, texels.TexelAt(2, 0, SpriteEffectTileOverlay.RingTexel));
        Assert.AreEqual(5, Fractions(texels, 2, 0).B);
        Assert.AreEqual(SpriteEffectTileOverlay.FractionByte(0.5f), Fractions(texels, 2, 0).R);
    }

    [TestMethod]
    public void TilesOutsideTheGrid_AreIgnored()
    {
        var texels = Begun(4, 3);

        texels.AddWash(Scroll.X - 1, Scroll.Y, Color.Red);
        texels.AddFill(Scroll.X + 4, Scroll.Y, Color.Red, 1f);
        texels.SetRing(Scroll.X, Scroll.Y + 3, Color.Red, 5);

        Assert.IsTrue(texels.IsEmpty);
        Assert.IsTrue(texels.Texels.Take(texels.Width * texels.Height).All(texel => texel == Color.Transparent));
    }

    [TestMethod]
    public void Begin_ClearsWhatTheLastFrameAdded()
    {
        var texels = Begun();
        texels.AddWash(Scroll.X + 3, Scroll.Y + 2, Color.Red);
        texels.AddFill(Scroll.X + 3, Scroll.Y + 2, Color.Red, 1f);

        texels.Begin(Scroll, 10, 6);

        Assert.IsTrue(texels.IsEmpty);
        Assert.IsTrue(texels.Texels.All(texel => texel == Color.Transparent));
    }
}
