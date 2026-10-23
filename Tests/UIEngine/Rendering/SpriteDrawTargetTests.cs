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
        public readonly List<(Rectangle Destination, Color Color, SpriteDrawStyle Style)> Rectangles = [];
        public readonly List<(string Text, Vector2 Position, Color Color, SpriteDrawStyle Style)> Strings = [];
        public readonly List<(string Text, Vector2 Position, Color FillColor, float AlphaMultiplier, SpriteDrawStyle Style)> OutlinedStrings = [];

        public void Draw(Texture2D texture, Rectangle destination, Rectangle? source, Color color, SpriteDrawStyle style) =>
            Rectangles.Add((destination, color, style));

        public void Draw(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, SpriteDrawStyle style) =>
            throw new AssertFailedException("No transformed draw expected.");

        public void DrawString(SpriteFontBase font, string text, Vector2 position, Color color, SpriteDrawStyle style) =>
            Strings.Add((text, position, color, style));

        public void DrawOutlinedString(SpriteFontBase font, string text, Vector2 position, Color fillColor, float alphaMultiplier, SpriteDrawStyle style) =>
            OutlinedStrings.Add((text, position, fillColor, alphaMultiplier, style));
    }

    private static readonly SpriteDrawStyle Style = new(SpriteDrawMode.Plain);

    /// <summary>The label leaves how to outline to the target -- a SpriteBatch's nine copies, the batch's two draws per glyph -- at a whole pixel.</summary>
    [TestMethod]
    public void OutlinedLabel_IsOneOutlinedString_RoundedToAWholePixel_WithTheStyle()
    {
        var target = new RecordingDrawTarget();

        LabelRenderer.Draw(target, TestFonts.Shared.GetFont(18), "g", new Vector2(10.4f, 20.6f), Color.Yellow, outline: true, alphaMultiplier: 0.5f, style: Style);

        Assert.AreEqual(0, target.Strings.Count);
        Assert.AreEqual(("g", new Vector2(10, 21), Color.Yellow, 0.5f, Style), target.OutlinedStrings.Single());
    }

    /// <summary>What a SpriteBatch draws for outlined text: eight black copies a pixel apart, then the fill, all faded together.</summary>
    [TestMethod]
    public void Copies_AreEightBlackCopiesThenTheFill_EachWithTheStyle()
    {
        var target = new RecordingDrawTarget();

        ContrastTextRenderer.DrawCopies(target, TestFonts.Shared.GetFont(18), "g", new Vector2(10, 21), Color.Yellow, 0.5f, Style);

        Assert.AreEqual(9, target.Strings.Count);
        Assert.IsTrue(target.Strings.Take(8).All(draw => draw.Color == ContrastTextRenderer.OutlineColor * 0.5f), "Outline copies first, faded.");
        Assert.AreEqual(8, target.Strings.Take(8).Select(draw => draw.Position).Distinct().Count(), "Eight different offsets.");
        Assert.IsTrue(target.Strings.Take(8).All(draw => System.Math.Abs(draw.Position.X - 10) <= 1 && System.Math.Abs(draw.Position.Y - 21) <= 1 && draw.Position != new Vector2(10, 21)), "Each a pixel from the fill.");
        Assert.AreEqual((Color.Yellow * 0.5f, new Vector2(10, 21)), (target.Strings[8].Color, target.Strings[8].Position), "The fill last, faded, in place.");
        Assert.IsTrue(target.Strings.All(draw => draw.Style == Style));
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

    [TestMethod]
    public void FadingGlow_DrawsItsRingsThroughTheTarget_WithTheStyle()
    {
        var target = new RecordingDrawTarget();

        GlowRenderer.Draw(target, null!, new Rectangle(0, 0, 32, 32), Color.LightGreen, GlowMode.InteriorFade, style: Style);

        Assert.AreEqual(GlowRenderer.FadeRingCount * 4, target.Rectangles.Count);
        Assert.IsTrue(target.Rectangles.All(draw => draw.Style == Style));
    }
}
