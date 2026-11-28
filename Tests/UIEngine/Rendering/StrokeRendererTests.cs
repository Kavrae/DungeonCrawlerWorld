using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Rendering;

namespace Tests.UIEngine.Rendering;

/// <summary>StrokeRenderer's strokes land where their geometry says, as turned strips on any ISpriteDrawTarget.</summary>
[TestClass]
public sealed class StrokeRendererTests
{
    private const float Tolerance = 0.001f;

    private sealed class StripRecordingTarget : ISpriteDrawTarget
    {
        public readonly List<(Vector2 Position, Vector2 Size, float Rotation, Vector2 Origin, Color Color)> Strips = [];

        public void Draw(Texture2D texture, Rectangle destination, Rectangle? source, Color color, SpriteDrawStyle style) => throw new AssertFailedException("A stroke draws no texture.");

        public void Draw(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, SpriteDrawStyle style) => throw new AssertFailedException("A stroke draws no texture.");

        public void FillRectangle(Rectangle destination, Color color, SpriteDrawStyle style) => throw new AssertFailedException("A stroke is a turned strip.");

        public void FillRectangle(Vector2 position, Vector2 size, float rotation, Vector2 normalizedOrigin, Color color, SpriteDrawStyle style) =>
            Strips.Add((position, size, rotation, normalizedOrigin, color));

        public void DrawString(SpriteFontBase font, string text, Vector2 position, Color color, SpriteDrawStyle style) => throw new AssertFailedException("A stroke draws no text.");

        public void DrawOutlinedString(SpriteFontBase font, string text, Vector2 position, Color fillColor, float alphaMultiplier, SpriteDrawStyle style) => throw new AssertFailedException("A stroke draws no text.");

        public Vector2 EndOf(int index)
        {
            var strip = Strips[index];
            return strip.Position + new Vector2(MathF.Cos(strip.Rotation), MathF.Sin(strip.Rotation)) * strip.Size.X;
        }
    }

    [TestMethod]
    public void Line_IsOneStripFromStartToEnd_CentredAcrossItsThickness()
    {
        var target = new StripRecordingTarget();

        StrokeRenderer.Line(target, new Vector2(10, 10), new Vector2(13, 14), 2f, Color.Red);

        Assert.AreEqual(1, target.Strips.Count);
        var strip = target.Strips[0];
        Assert.AreEqual(new Vector2(10, 10), strip.Position);
        Assert.AreEqual(5f, strip.Size.X, Tolerance);
        Assert.AreEqual(2f, strip.Size.Y, Tolerance);
        Assert.AreEqual(MathF.Atan2(4, 3), strip.Rotation, Tolerance);
        Assert.AreEqual(new Vector2(0f, 0.5f), strip.Origin);
        Assert.AreEqual(Color.Red, strip.Color);
    }

    [TestMethod]
    public void Line_OfNoLength_DrawsNothing()
    {
        var target = new StripRecordingTarget();

        StrokeRenderer.Line(target, new Vector2(4, 4), new Vector2(4, 4), 1f, Color.White);

        Assert.AreEqual(0, target.Strips.Count);
    }

    [TestMethod]
    public void PatternedLine_DrawsDashesAlongTheLine_CuttingTheLastShortAtTheEnd()
    {
        var target = new StripRecordingTarget();

        StrokeRenderer.PatternedLine(target, Vector2.Zero, new Vector2(20, 0), 1f, StrokePattern.Dashed(6f, 2f), Color.White);

        CollectionAssert.AreEqual(new[] { 0f, 8f, 16f }, target.Strips.Select(strip => strip.Position.X).ToArray());
        CollectionAssert.AreEqual(new[] { 6f, 6f, 4f }, target.Strips.Select(strip => strip.Size.X).ToArray());
    }

    [TestMethod]
    public void PatternedLine_AsACentreLine_AlternatesDashAndDot()
    {
        var target = new StripRecordingTarget();

        StrokeRenderer.PatternedLine(target, Vector2.Zero, new Vector2(30, 0), 1f, StrokePattern.CentreLine(10f, 3f, 2f), Color.White);

        CollectionAssert.AreEqual(new[] { 10f, 2f, 10f }, target.Strips.Select(strip => strip.Size.X).ToArray());
    }

    [TestMethod]
    public void ArcSegmentCount_GrowsWithTheRadius_AndNeverFallsBelowTheMinimum()
    {
        Assert.AreEqual(StrokeRenderer.MinimumArcSegmentCount, StrokeRenderer.ArcSegmentCount(1f, MathHelper.TwoPi));
        Assert.IsTrue(StrokeRenderer.ArcSegmentCount(100f, MathHelper.TwoPi) > StrokeRenderer.ArcSegmentCount(20f, MathHelper.TwoPi));
        Assert.IsTrue(MathHelper.TwoPi * 100f / StrokeRenderer.ArcSegmentCount(100f, MathHelper.TwoPi) <= StrokeRenderer.MaximumArcSegmentLength);
    }

    [TestMethod]
    public void CircleOutline_ClosesOnItself_WithEverySegmentEndOnTheCircle()
    {
        var target = new StripRecordingTarget();
        var centre = new Vector2(50, 50);

        StrokeRenderer.CircleOutline(target, centre, 30f, 1f, Color.White);

        Assert.AreEqual(StrokeRenderer.ArcSegmentCount(30f, MathHelper.TwoPi), target.Strips.Count);
        for (var index = 0; index < target.Strips.Count; index++)
        {
            Assert.AreEqual(30f, Vector2.Distance(centre, target.Strips[index].Position), 0.01f);
            Assert.AreEqual(30f, Vector2.Distance(centre, target.EndOf(index)), 0.01f);
        }

        Assert.AreEqual(0f, Vector2.Distance(target.Strips[0].Position, target.EndOf(target.Strips.Count - 1)), 0.01f);
    }

    [TestMethod]
    public void OpenArrowhead_IsTwoStrokesFromTheTip_SymmetricAboutTheLine()
    {
        var target = new StripRecordingTarget();
        var tip = new Vector2(40, 20);

        StrokeRenderer.OpenArrowhead(target, tip, 0f, 6f, MathHelper.Pi / 6f, 1f, Color.White);

        Assert.AreEqual(2, target.Strips.Count);
        Assert.AreEqual(tip, target.Strips[0].Position);
        Assert.AreEqual(tip, target.Strips[1].Position);
        var first = target.EndOf(0);
        var second = target.EndOf(1);
        Assert.IsTrue(first.X < tip.X);
        Assert.AreEqual(first.X, second.X, Tolerance);
        Assert.AreEqual(tip.Y - first.Y, second.Y - tip.Y, Tolerance);
    }

    [TestMethod]
    public void FillDiamond_IsASquareTurnedAQuarter_ReachingHalfDiagonalFromItsCentre()
    {
        var target = new StripRecordingTarget();

        StrokeRenderer.FillDiamond(target, new Vector2(20, 20), 10f, Color.White);

        Assert.AreEqual(1, target.Strips.Count);
        var strip = target.Strips[0];
        Assert.AreEqual(new Vector2(20, 20), strip.Position);
        Assert.AreEqual(new Vector2(0.5f, 0.5f), strip.Origin);
        Assert.AreEqual(MathHelper.PiOver4, strip.Rotation, Tolerance);
        Assert.AreEqual(10f, strip.Size.X * MathF.Sqrt(2f) / 2f, Tolerance);
        Assert.AreEqual(strip.Size.X, strip.Size.Y, Tolerance);
    }

    [TestMethod]
    public void DiamondOutline_JoinsItsFourVertices()
    {
        var target = new StripRecordingTarget();
        var centre = new Vector2(20, 20);

        StrokeRenderer.DiamondOutline(target, centre, 10f, 1f, Color.White);

        Assert.AreEqual(4, target.Strips.Count);
        for (var index = 0; index < 4; index++)
        {
            Assert.AreEqual(10f, Vector2.Distance(centre, target.Strips[index].Position), Tolerance);
            Assert.AreEqual(0f, Vector2.Distance(target.EndOf(index), target.Strips[(index + 1) % 4].Position), 0.01f);
        }
    }

    [TestMethod]
    public void BreakMark_IsTwoParallelSlashes_SpacingApartAlongTheLine()
    {
        var target = new StripRecordingTarget();

        StrokeRenderer.BreakMark(target, new Vector2(50, 10), 0f, 5f, 6f, 1f, Color.White);

        Assert.AreEqual(2, target.Strips.Count);
        Assert.AreEqual(target.Strips[0].Rotation, target.Strips[1].Rotation, Tolerance);
        var firstMiddle = (target.Strips[0].Position + target.EndOf(0)) / 2f;
        var secondMiddle = (target.Strips[1].Position + target.EndOf(1)) / 2f;
        Assert.AreEqual(6f, secondMiddle.X - firstMiddle.X, Tolerance);
        Assert.AreEqual(10f, firstMiddle.Y, Tolerance);
    }

    [TestMethod]
    public void HatchRectangle_KeepsEveryLineInsideTheArea()
    {
        var target = new StripRecordingTarget();
        var topLeft = new Vector2(10, 20);
        var size = new Vector2(30, 12);

        StrokeRenderer.HatchRectangle(target, topLeft, size, 4f, 1f, Color.White);

        Assert.IsTrue(target.Strips.Count > 0);
        for (var index = 0; index < target.Strips.Count; index++)
        {
            foreach (var point in new[] { target.Strips[index].Position, target.EndOf(index) })
            {
                Assert.IsTrue(point.X >= topLeft.X - Tolerance && point.X <= topLeft.X + size.X + Tolerance, $"x {point.X}");
                Assert.IsTrue(point.Y >= topLeft.Y - Tolerance && point.Y <= topLeft.Y + size.Y + Tolerance, $"y {point.Y}");
            }
        }
    }

    [TestMethod]
    public void FillCircle_SweepsItsSliversAllTheWayAround()
    {
        var target = new StripRecordingTarget();

        StrokeRenderer.FillCircle(target, new Vector2(8, 8), 5f, Color.Gold);

        Assert.AreEqual(StrokeRenderer.CircleSliverCount, target.Strips.Count);
        Assert.IsTrue(target.Strips.TrueForAll(strip => strip.Position == new Vector2(8, 8) && strip.Size.X == 5f));
    }
}
