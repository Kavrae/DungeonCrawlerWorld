using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine.Rendering;

/// <summary>The drawing helpers draw the same way to any ISpriteDrawTarget, and hand the style they are given to every draw.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SpriteDrawTargetTests
{
    private sealed class RecordingDrawTarget : ISpriteDrawTarget
    {
        public readonly List<(Rectangle Destination, Rectangle? Source, Color Color, SpriteDrawStyle Style)> Rectangles = [];
        public readonly List<(Vector2 Position, Rectangle? Source, Color Color, float Rotation, Vector2 Origin, Vector2 Scale, SpriteEffects Effects, SpriteDrawStyle Style)> TransformedDraws = [];
        public readonly List<(string Text, Vector2 Position, Color Color, SpriteDrawStyle Style)> Strings = [];
        public readonly List<(string Text, Vector2 Position, Color FillColor, float AlphaMultiplier, SpriteDrawStyle Style)> OutlinedStrings = [];

        public void Draw(Texture2D texture, Rectangle destination, Rectangle? source, Color color, SpriteDrawStyle style) =>
            Rectangles.Add((destination, source, color, style));

        public void Draw(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, SpriteDrawStyle style) =>
            TransformedDraws.Add((position, source, color, rotation, origin, scale, effects, style));

        public void DrawString(SpriteFontBase font, string text, Vector2 position, Color color, SpriteDrawStyle style) =>
            Strings.Add((text, position, color, style));

        public void DrawOutlinedString(SpriteFontBase font, string text, Vector2 position, Color fillColor, float alphaMultiplier, SpriteDrawStyle style) =>
            OutlinedStrings.Add((text, position, fillColor, alphaMultiplier, style));
    }

    private static readonly SpriteDrawStyle Style = new(SpriteDrawMode.Plain);

    /// <summary>The label leaves how to outline to the target -- the batch's two draws per glyph -- at a whole pixel.</summary>
    [TestMethod]
    public void OutlinedLabel_IsOneOutlinedString_RoundedToAWholePixel_WithTheStyle()
    {
        var target = new RecordingDrawTarget();

        LabelRenderer.Draw(target, TestFonts.Shared.GetFont(18), "g", new Vector2(10.4f, 20.6f), Color.Yellow, outline: true, alphaMultiplier: 0.5f, style: Style);

        Assert.AreEqual(0, target.Strings.Count);
        Assert.AreEqual(("g", new Vector2(10, 21), Color.Yellow, 0.5f, Style), target.OutlinedStrings.Single());
    }

    /// <summary>Contrast text (a countdown, a key label) is one outlined string, rounded, white by default, faded fill and outline together.</summary>
    [TestMethod]
    public void ContrastText_IsOneOutlinedString_RoundedAndWhiteByDefault()
    {
        var target = new RecordingDrawTarget();

        ContrastTextRenderer.Draw(target, TestFonts.Shared.GetFont(18), "12", new Vector2(4.6f, 7.2f), alphaMultiplier: 0.5f);

        Assert.AreEqual(0, target.Strings.Count);
        Assert.AreEqual(("12", new Vector2(5, 7), ContrastTextRenderer.FillColor, 0.5f, SpriteDrawStyle.Plain), target.OutlinedStrings.Single());
    }

    [TestMethod]
    public void PlainLabel_IsOneDraw_WithTheStyle()
    {
        var target = new RecordingDrawTarget();

        LabelRenderer.Draw(target, TestFonts.Shared.GetFont(18), "g", new Vector2(10, 20), Color.Yellow, style: Style);

        Assert.AreEqual(1, target.Strings.Count);
        Assert.AreEqual(Style, target.Strings[0].Style);
    }

    [TestMethod]
    public void GlyphWithNoSprite_IsDrawnCentredThroughTheTarget_WithTheStyle()
    {
        var target = new RecordingDrawTarget();
        var drawn = SpriteOrGlyphRenderer.Draw(target, new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver), null, TestFonts.Shared.GetTextMeasurer(TestFonts.Shared.GetFont(18)), "g", Color.Red, new Vector2(0, 0), new Vector2(32, 32), Color.White, outline: false, style: Style);

        Assert.IsTrue(drawn);
        Assert.AreEqual(1, target.Strings.Count);
        Assert.AreEqual(Style, target.Strings[0].Style);
    }

    /// <summary>SpriteBatch's rectangle shapes reach the target's rectangle draw unchanged, plain unless a style is given.</summary>
    [TestMethod]
    public void SpriteBatchRectangleShapes_AreTheTargetsRectangleDraw()
    {
        var target = new RecordingDrawTarget();
        var destination = new Rectangle(3, 4, 30, 40);
        var source = new Rectangle(16, 0, 16, 16);

        target.Draw(null!, destination, Color.Red);
        target.Draw(null!, destination, Color.Red, Style with { Mode = SpriteDrawMode.Desaturate });
        target.Draw(null!, destination, source, Color.Red);

        CollectionAssert.AreEqual(
            new[]
            {
                (destination, (Rectangle?)null, Color.Red, SpriteDrawStyle.Plain),
                (destination, (Rectangle?)null, Color.Red, SpriteDrawStyle.Desaturated),
                (destination, (Rectangle?)source, Color.Red, SpriteDrawStyle.Plain),
            },
            target.Rectangles);
        Assert.AreEqual(0, target.TransformedDraws.Count);
    }

    /// <summary>SpriteBatch's position shapes are the target's transformed draw with SpriteBatch's defaults: unturned, origin at the top-left, unscaled, unflipped.</summary>
    [TestMethod]
    public void SpriteBatchPositionShapes_AreTheTransformedDraw_WithSpriteBatchsDefaults()
    {
        var target = new RecordingDrawTarget();
        var position = new Vector2(5, 6);
        var source = new Rectangle(0, 16, 16, 16);

        target.Draw(null!, position, Color.Blue);
        target.Draw(null!, position, source, Color.Blue);
        target.Draw(null!, position, source, Color.Blue, 0.5f, new Vector2(8, 8), 2f, SpriteEffects.FlipHorizontally);
        target.Draw(null!, position, source, Color.Blue, 0.5f, new Vector2(8, 8), new Vector2(2, 3), SpriteEffects.FlipVertically);

        CollectionAssert.AreEqual(
            new[]
            {
                (position, (Rectangle?)null, Color.Blue, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, SpriteDrawStyle.Plain),
                (position, (Rectangle?)source, Color.Blue, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, SpriteDrawStyle.Plain),
                (position, (Rectangle?)source, Color.Blue, 0.5f, new Vector2(8, 8), new Vector2(2, 2), SpriteEffects.FlipHorizontally, SpriteDrawStyle.Plain),
                (position, (Rectangle?)source, Color.Blue, 0.5f, new Vector2(8, 8), new Vector2(2, 3), SpriteEffects.FlipVertically, SpriteDrawStyle.Plain),
            },
            target.TransformedDraws);
    }

    [TestMethod]
    public void SpriteBatchDrawString_IsThePlainString()
    {
        var target = new RecordingDrawTarget();

        target.DrawString(TestFonts.Shared.GetFont(18), "g", new Vector2(1, 2), Color.Green);

        Assert.AreEqual(("g", new Vector2(1, 2), Color.Green, SpriteDrawStyle.Plain), target.Strings.Single());
    }

    [TestMethod]
    public void FadingGlow_DrawsItsRingsThroughTheTarget_WithTheStyle()
    {
        var target = new RecordingDrawTarget();

        GlowRenderer.Draw(target, null!, new Rectangle(0, 0, 32, 32), Color.LightGreen, GlowMode.InteriorFade, style: Style);

        Assert.AreEqual(GlowRenderer.FadeRingCount * 4, target.Rectangles.Count);
        Assert.IsTrue(target.Rectangles.All(draw => draw.Style == Style));
    }
}
