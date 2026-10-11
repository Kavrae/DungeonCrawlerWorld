using System.Text.Json;
using Engine.Math;
using Game.Sprites;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Rendering;

namespace Tests.Presentation.Rendering;

[TestClass]
public sealed class SpritePlacementMathTests
{
    private const float Tolerance = 0.0001f;

    private static readonly Vector2Byte OneByOne = new(1, 1);

    [TestMethod]
    public void DrawnRectangle_NoPixelsPerTile_IsTheFootprintExactly()
    {
        var (topLeft, size) = SpritePlacementMath.DrawnRectangle(new Vector2(40, 60), new Vector2(64, 64), new Vector2Byte(2, 2), new Point(64, 64), placement: null);

        Assert.AreEqual(new Vector2(40, 60), topLeft);
        Assert.AreEqual(new Vector2(64, 64), size);
    }

    [TestMethod]
    [DataRow(16)]
    [DataRow(32)]
    [DataRow(48)]
    public void DrawnRectangle_A64PixelCellAt32PixelsPerTile_IsTwoByTwoTilesAtEveryZoom(int tilePixels)
    {
        var (_, size) = SpritePlacementMath.DrawnRectangle(Vector2.Zero, new Vector2(tilePixels, tilePixels), OneByOne, new Point(64, 64), new SpritePlacement(32, 32, 64));

        Assert.AreEqual(new Vector2(2 * tilePixels, 2 * tilePixels), size);
    }

    [TestMethod]
    [DataRow(32, 64)]
    [DataRow(20, 52)]
    [DataRow(40, 30)]
    public void DrawnRectangle_ThePivot_LandsOnTheFootprintsBottomCentre(int pivotX, int pivotY)
    {
        var footprintTopLeft = new Vector2(100, 200);
        var footprintSize = new Vector2(32, 32);
        var placement = new SpritePlacement(32, pivotX, pivotY);

        var (topLeft, size) = SpritePlacementMath.DrawnRectangle(footprintTopLeft, footprintSize, OneByOne, new Point(64, 64), placement);

        var pivotOnScreen = new Vector2(topLeft.X + pivotX * size.X / 64f, topLeft.Y + pivotY * size.Y / 64f);
        Assert.AreEqual(footprintTopLeft.X + 16, pivotOnScreen.X, Tolerance);
        Assert.AreEqual(footprintTopLeft.Y + 32, pivotOnScreen.Y, Tolerance);
    }

    [TestMethod]
    public void DrawnTiles_PlacementScalesPerAxisWithTheFootprint()
    {
        var placement = new SpritePlacement(16, 8, 16);
        var source = new Point(16, 16);

        var oneByOne = SpritePlacementMath.DrawnTiles(OneByOne, source, placement);
        var twoByTwo = SpritePlacementMath.DrawnTiles(new Vector2Byte(2, 2), source, placement);
        var oneByTwo = SpritePlacementMath.DrawnTiles(new Vector2Byte(1, 2), source, placement);

        Assert.AreEqual(new SpritePlacementMath.TileArea(0, 0, 1, 1), oneByOne);
        Assert.AreEqual(new SpritePlacementMath.TileArea(0, 0, 2, 2), twoByTwo);
        Assert.AreEqual(new SpritePlacementMath.TileArea(0, 0, 1, 2), oneByTwo);
    }

    [TestMethod]
    public void DrawnTiles_InsideTheReachBoxAtOneByOne_ButNotAtThreeByThree_IsShrunkAboutItsPivotToFitExactly()
    {
        var placement = new SpritePlacement(16, 8, 48);
        var source = new Point(16, 48);

        var atOneByOne = SpritePlacementMath.DrawnTiles(OneByOne, source, placement);
        var atHuge = SpritePlacementMath.DrawnTiles(new Vector2Byte(3, 3), source, placement);

        Assert.AreEqual(new SpritePlacementMath.TileArea(0, -2, 1, 1), atOneByOne);
        Assert.AreEqual(0, atHuge.Top, Tolerance);
        Assert.AreEqual(3, atHuge.Bottom, Tolerance);
        Assert.AreEqual(1, atHuge.Left, Tolerance);
        Assert.AreEqual(2, atHuge.Right, Tolerance);
    }

    [TestMethod]
    public void DrawnTiles_AlreadyInsideTheReachBoxAtThreeByThree_IsUntouched()
    {
        var area = SpritePlacementMath.DrawnTiles(new Vector2Byte(3, 3), new Point(16, 16), new SpritePlacement(16, 8, 16));

        Assert.AreEqual(new SpritePlacementMath.TileArea(0, 0, 3, 3), area);
    }

    /// <summary>The row walk overscans by the reach box, so nothing in the checked-in manifest may draw outside it at any footprint up to Huge.</summary>
    [TestMethod]
    public void DrawnTiles_EveryCheckedInPlacedCell_StaysInsideTheReachBoxAtEveryFootprintUpToHuge()
    {
        var entries = JsonSerializer.Deserialize<List<SpriteManifestEntry>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SpriteManifest.json")))!;
        var parts = entries.Select(entry => (entry.Name, Part: entry.Face)).Concat(entries.Where(entry => entry.Top is not null).Select(entry => (entry.Name, Part: entry.Top!)));

        foreach (var (name, part) in parts)
        {
            foreach (var cell in part.Cells.OfType<SpriteManifestCell>())
            {
                for (byte width = 1; width <= 3; width++)
                {
                    for (byte height = 1; height <= 3; height++)
                    {
                        var footprint = new Vector2Byte(width, height);
                        var area = SpritePlacementMath.DrawnTiles(footprint, new Point(cell.SourceWidth, cell.SourceHeight), part.PlacementFor(cell));

                        var context = $"{name} at {width}x{height}: {area}";
                        Assert.IsGreaterThanOrEqualTo(-SpriteReach.MaximumColumnsBeside - Tolerance, area.Left, context);
                        Assert.IsLessThanOrEqualTo(1 + SpriteReach.MaximumColumnsBeside + Tolerance, area.Right, context);
                        Assert.IsGreaterThanOrEqualTo(height - 1 - SpriteReach.MaximumRowsAbove - Tolerance, area.Top, context);
                        Assert.IsLessThanOrEqualTo(height + Tolerance, area.Bottom, context);
                    }
                }
            }
        }
    }

    [TestMethod]
    public void CueTop_ClampsToNoLowerThanTheFootprintTop()
    {
        Assert.AreEqual(40f, SpritePlacementMath.CueTop(drawnTop: 40, footprintTop: 100));
        Assert.AreEqual(100f, SpritePlacementMath.CueTop(drawnTop: 120, footprintTop: 100));
    }

    [TestMethod]
    public void TransformedDrawArguments_TurnAndFlips_ReachSpriteBatchAsRotationAndEffects()
    {
        var transform = new SpriteCellTransform(90, FlipHorizontal: true, FlipVertical: false);

        var arguments = TransformedDrawArguments.For(transform, new Rectangle(0, 0, 16, 16), new Vector2(10, 20), new Vector2(32, 32));

        Assert.AreEqual(MathHelper.PiOver2, arguments.Rotation, Tolerance);
        Assert.AreEqual(SpriteEffects.FlipHorizontally, arguments.Effects);
        Assert.AreEqual(new Vector2(26, 36), arguments.Position);
        Assert.AreEqual(new Vector2(8, 8), arguments.Origin);
        Assert.AreEqual(new Vector2(2, 2), arguments.Scale);
    }

    [TestMethod]
    public void TransformedDrawArguments_QuarterTurnOfANonSquareDestination_TakesEachScaleFromTheOtherAxis()
    {
        var arguments = TransformedDrawArguments.For(new SpriteCellTransform(270, false, true), new Rectangle(0, 0, 16, 16), Vector2.Zero, new Vector2(32, 64));

        Assert.AreEqual(new Vector2(4, 2), arguments.Scale);
        Assert.AreEqual(SpriteEffects.FlipVertically, arguments.Effects);
    }
}
