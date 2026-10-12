using SpriteFormat;

namespace Tests.SpriteManifestFormat;

[TestClass]
public sealed class SpriteAuthoringTests
{
    private static SpriteManifestCell Square() => new("sheet.png", 0, 0, 16, 16);

    [TestMethod]
    public void RotatedClockwise_FourTimes_IsUnturned()
    {
        var cell = Square().RotatedClockwise().RotatedClockwise().RotatedClockwise().RotatedClockwise();

        Assert.AreEqual(Square(), cell);
    }

    [TestMethod]
    public void RotatedCounterClockwise_UndoesRotatedClockwise()
    {
        Assert.AreEqual(Square(), Square().RotatedClockwise().RotatedCounterClockwise());
    }

    [TestMethod]
    public void BothMirrors_AreWrittenAsAHalfTurn()
    {
        var cell = Square().MirroredHorizontally().MirroredVertically();

        Assert.AreEqual(Square() with { Rotation = 180 }, cell);
    }

    [TestMethod]
    public void MirroredHorizontally_OfAQuarterTurn_ReversesTheTurnAndFlips()
    {
        var cell = Square().RotatedClockwise().MirroredHorizontally();

        Assert.AreEqual(Square() with { Rotation = 270, FlipHorizontal = true }, cell);
    }

    [TestMethod]
    public void WithoutTransform_ClearsTurnsAndFlipsButKeepsTheTint()
    {
        var cell = (Square() with { Tint = "#FF0000" }).RotatedClockwise().MirroredVertically().WithoutTransform();

        Assert.AreEqual(Square() with { Tint = "#FF0000" }, cell);
    }

    [TestMethod]
    public void Spanning_CornersInEitherOrder_CoverTheSameCellsWithASpanOfOneWrittenAsAbsent()
    {
        var forward = SpriteManifestCell.Spanning("sheet.png", 2, 3, 4, 3, 16, 16);
        var backward = SpriteManifestCell.Spanning("sheet.png", 4, 3, 2, 3, 16, 16);

        Assert.AreEqual(forward, backward);
        Assert.AreEqual(new SpriteManifestCell("sheet.png", 2, 3, 16, 16, ColumnSpan: 3), forward);
    }

    [TestMethod]
    public void CanQuarterTurn_OnlyASquareCell()
    {
        Assert.IsTrue(Square().CanQuarterTurn);
        Assert.IsFalse((Square() with { RowSpan = 2 }).CanQuarterTurn);
    }

    [TestMethod]
    [DataRow(SpriteLinks.North | SpriteLinks.East, SpriteLinks.North | SpriteLinks.West)]
    [DataRow(SpriteLinks.East, SpriteLinks.West)]
    [DataRow(SpriteLinks.North | SpriteLinks.South, SpriteLinks.North | SpriteLinks.South)]
    public void MirroredLeftRight_Cardinal_SwapsEastAndWest(int mask, int expected)
    {
        Assert.AreEqual(expected, SpriteLinks.MirroredLeftRight(SpriteLinking.Cardinal, mask));
    }

    [TestMethod]
    [DataRow(SpriteLinks.HorizontalEast, SpriteLinks.HorizontalWest)]
    [DataRow(SpriteLinks.Standalone, SpriteLinks.Standalone)]
    [DataRow(SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest, SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest)]
    public void MirroredLeftRight_Horizontal_SwapsTheEnds(int mask, int expected)
    {
        Assert.AreEqual(expected, SpriteLinks.MirroredLeftRight(SpriteLinking.Horizontal, mask));
    }

    [TestMethod]
    public void MirroredTopBottom_SwapsNorthAndSouth()
    {
        Assert.AreEqual(SpriteLinks.South | SpriteLinks.East, SpriteLinks.MirroredTopBottom(SpriteLinks.North | SpriteLinks.East));
    }

    [TestMethod]
    public void TurnedClockwise_MovesEachJoinOneSideRound_AndFourTurnsComeBack()
    {
        var mask = SpriteLinks.North | SpriteLinks.West;

        Assert.AreEqual(SpriteLinks.East | SpriteLinks.North, SpriteLinks.TurnedClockwise(mask));
        Assert.AreEqual(mask, SpriteLinks.TurnedClockwise(SpriteLinks.TurnedClockwise(SpriteLinks.TurnedClockwise(SpriteLinks.TurnedClockwise(mask)))));
    }

    [TestMethod]
    public void DrawnTiles_Stretched_FillsTheFootprint_RaisedByItsRows()
    {
        Assert.AreEqual(new SpriteTileArea(0, 0, 2, 2), SpritePlacementTiles.DrawnTiles(2, 2, 16, 16, placement: null));
        Assert.AreEqual(new SpriteTileArea(0, -2, 1, -1), SpritePlacementTiles.DrawnTiles(1, 1, 16, 16, placement: null, raisedRows: 2));
    }

    [TestMethod]
    public void DrawnTiles_TwoRowsTallAtOneByOne_ReachesTheRowAbove()
    {
        var area = SpritePlacementTiles.DrawnTiles(1, 1, 16, 32, new SpritePlacement(16, 8, 32));

        Assert.AreEqual(new SpriteTileArea(0, -1, 1, 1), area);
    }

    [TestMethod]
    public void DrawnTiles_AtOneByOne_IsNeverShrunk_SoABreachShows()
    {
        var area = SpritePlacementTiles.DrawnTiles(1, 1, 16, 64, new SpritePlacement(16, 8, 64));

        Assert.AreEqual(new SpriteTileArea(0, -3, 1, 1), area);
    }

    [TestMethod]
    public void DrawnTiles_ScaledPastTheReachBox_IsShrunkAboutItsPivotToFit()
    {
        var area = SpritePlacementTiles.DrawnTiles(3, 3, 16, 48, new SpritePlacement(16, 8, 48));
        var reachBox = SpritePlacementTiles.ReachBox(3);

        Assert.AreEqual(reachBox.Top, area.Top, 0.0001f);
        Assert.AreEqual(3f, area.Bottom, 0.0001f);
        Assert.AreEqual(1.5f, (area.Left + area.Right) / 2, 0.0001f);
    }

    [TestMethod]
    public void ReachBox_IsTwoRowsAboveTheAnchorTileAndTwoColumnsEitherSide()
    {
        Assert.AreEqual(new SpriteTileArea(-2, -2, 3, 1), SpritePlacementTiles.ReachBox(1));
        Assert.AreEqual(new SpriteTileArea(-2, 0, 3, 3), SpritePlacementTiles.ReachBox(3));
    }
}
