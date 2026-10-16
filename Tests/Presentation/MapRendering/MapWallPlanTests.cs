using Microsoft.Xna.Framework;
using Presentation.MapRendering;
using Presentation.Rendering;
using SpriteFormat;
using UIEngine.Rendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class MapWallPlanTests
{
    private const int GridColumns = 20;
    private const int GridRows = 10;
    private static readonly Point Scroll = new(10, 10);

    /// <summary>A wall with a one-row face and no top.</summary>
    private const string FaceOnly = "face-only";

    /// <summary>A wall with a one-row face and a top.</summary>
    private const string FaceAndTop = "face-and-top";

    /// <summary>A wall with a two-row face (16x32 at 16 pixels per tile) and a top.</summary>
    private const string TwoRowFaceAndTop = "two-row-face-and-top";

    private static readonly SpriteManifestCell FaceCell = new("sheet.png", 0, 0, 16, 16);
    private static readonly SpriteManifestCell TwoRowFaceCell = new("sheet.png", 1, 0, 16, 32);
    private static readonly SpriteManifestCell TopCell = new("sheet.png", 2, 0, 16, 16);

    private static readonly SpriteManifestPart TopPart = new([.. Enumerable.Repeat<SpriteManifestCell?>(TopCell, 16)], Linking: SpriteLinking.Cardinal);

    private static readonly SpriteManifestResolver Sprites = new(SpriteManifest.Parse(SpriteManifest.Serialize(
    [
        new SpriteManifestEntry(FaceOnly, [FaceCell]),
        new SpriteManifestEntry(FaceAndTop, [FaceCell], Top: TopPart),
        new SpriteManifestEntry(TwoRowFaceAndTop, [TwoRowFaceCell], PixelsPerTile: 16, Top: TopPart),
    ])));

    private static readonly MapVisual Face = new(SpriteManifestResolver.ToSpriteView(FaceCell, placement: null), string.Empty, default);
    private static readonly MapVisual TwoRowFace = new(SpriteManifestResolver.ToSpriteView(TwoRowFaceCell, new SpritePlacement(16, 8, 32)), string.Empty, default);
    private static readonly MapVisual Top = new(SpriteManifestResolver.ToSpriteView(TopCell, placement: null), string.Empty, default);

    private static MapWallPlan Plan(FakeMapView map, Point? scroll = null)
    {
        var cells = new MapTerrainCells(map, Sprites);
        cells.Read(scroll ?? Scroll, GridColumns, GridRows, mapLayer: 0);
        var plan = new MapWallPlan();
        plan.Plan(cells, scroll ?? Scroll, GridColumns, GridRows);
        return plan;
    }

    private static MapOverhang Piece(int wallX, int wallY, int anchorY, MapVisual visual, bool isTop) =>
        new(wallX, wallY, wallX, anchorY, visual, isTop);

    [TestMethod]
    public void Plan_AWallUnderOpenFloor_HasItsTopLive_AndItsOneRowFaceCached()
    {
        var map = new FakeMapView();
        map.AddWall(15, 15, FaceAndTop);

        var plan = Plan(map);

        CollectionAssert.AreEqual(new[] { Piece(15, 15, 15, Face, isTop: false) }, plan.CachedPieces.ToArray());
        CollectionAssert.AreEqual(new[] { Piece(15, 15, 14, Top, isTop: true) }, plan.Overhangs.ToArray());
    }

    [TestMethod]
    public void Plan_AThickWall_ShowsTopsWithOneFaceRow_TheInnerTopCached()
    {
        var map = new FakeMapView();
        map.AddWall(15, 14, FaceAndTop);
        map.AddWall(15, 15, FaceAndTop);

        var plan = Plan(map);

        CollectionAssert.AreEqual(new[] { Piece(15, 15, 15, Face, isTop: false), Piece(15, 15, 14, Top, isTop: true) }, plan.CachedPieces.ToArray());
        CollectionAssert.AreEqual(new[] { Piece(15, 14, 13, Top, isTop: true) }, plan.Overhangs.ToArray());
    }

    [TestMethod]
    public void Plan_AWallUnderAnOffMapTile_HasItsTopCached()
    {
        var map = new FakeMapView();
        map.AddWall(5, 0, FaceAndTop);

        var plan = Plan(map, new Point(0, 0));

        Assert.IsEmpty(plan.Overhangs);
        CollectionAssert.Contains(plan.CachedPieces.ToArray(), Piece(5, 0, -1, Top, isTop: true));
    }

    [TestMethod]
    public void Plan_ATwoRowFaceWithOpenFloorAbove_IsLive_AndItsTopIsRaisedAboveIt()
    {
        var map = new FakeMapView();
        map.AddWall(15, 15, TwoRowFaceAndTop);

        var plan = Plan(map);

        Assert.IsEmpty(plan.CachedPieces);
        CollectionAssert.AreEqual(new[] { Piece(15, 15, 15, TwoRowFace, isTop: false), Piece(15, 15, 13, Top, isTop: true) }, plan.Overhangs.ToArray());
    }

    [TestMethod]
    public void Plan_ATwoRowFaceUnderAnotherWall_IsCached()
    {
        var map = new FakeMapView();
        map.AddWall(15, 14, TwoRowFaceAndTop);
        map.AddWall(15, 15, TwoRowFaceAndTop);

        var plan = Plan(map);

        CollectionAssert.Contains(plan.CachedPieces.ToArray(), Piece(15, 15, 15, TwoRowFace, isTop: false));
    }

    [TestMethod]
    public void Plan_ATopIsRaisedByItsFacesHeight_WhetherOrNotItsTileDrawsAFace()
    {
        var map = new FakeMapView();
        map.AddWall(15, 14, TwoRowFaceAndTop);
        map.AddWall(15, 15, TwoRowFaceAndTop);

        var tops = Plan(map) is var plan ? plan.CachedPieces.Concat(plan.Overhangs).Where(piece => piece.IsTop).ToList() : [];

        CollectionAssert.AreEquivalent(new[] { Piece(15, 14, 12, Top, isTop: true), Piece(15, 15, 13, Top, isTop: true) }, tops);
    }

    [TestMethod]
    public void Plan_AWallWithNoTop_DrawsItsFaceOnEveryTile()
    {
        var map = new FakeMapView();
        map.AddWall(15, 14, FaceOnly);
        map.AddWall(15, 15, FaceOnly);

        var plan = Plan(map);

        CollectionAssert.AreEquivalent(new[] { Piece(15, 14, 14, Face, isTop: false), Piece(15, 15, 15, Face, isTop: false) }, plan.CachedPieces.ToArray());
        Assert.IsEmpty(plan.Overhangs);
    }

    [TestMethod]
    public void Plan_CachedTops_ComeAfterEveryCachedFace()
    {
        var map = new FakeMapView();
        for (var x = 12; x <= 14; x++)
        {
            map.AddWall(x, 14, FaceAndTop);
            map.AddWall(x, 15, FaceAndTop);
        }

        var cached = Plan(map).CachedPieces;

        var lastFace = cached.ToList().FindLastIndex(piece => !piece.IsTop);
        var firstTop = cached.ToList().FindIndex(piece => piece.IsTop);
        Assert.IsLessThan(firstTop, lastFace);
    }

    [TestMethod]
    public void Plan_Overhangs_AreSortedByRowThenFacesBeforeTopsThenColumn()
    {
        var map = new FakeMapView();
        map.AddWall(18, 15, TwoRowFaceAndTop);
        map.AddWall(12, 15, TwoRowFaceAndTop);
        map.AddWall(14, 12, FaceAndTop);

        var overhangs = Plan(map).Overhangs;

        var expected = overhangs.OrderBy(piece => piece.WallY).ThenBy(piece => piece.IsTop).ThenBy(piece => piece.WallX).ToList();
        CollectionAssert.AreEqual(expected, overhangs.ToList());
        Assert.AreEqual(12, overhangs[0].WallY);
    }

    [TestMethod]
    [DataRow(21, true)]
    [DataRow(22, false)]
    public void Plan_AWallBelowTheGrid_IsPlannedOnlyWithinTheOverscan(int wallY, bool expected)
    {
        var map = new FakeMapView();
        map.AddWall(15, wallY, TwoRowFaceAndTop);

        var plan = Plan(map);

        Assert.AreEqual(expected, plan.Overhangs.Concat(plan.CachedPieces).Any(piece => piece.WallY == wallY));
    }
}
