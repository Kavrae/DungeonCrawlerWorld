using Game.Sprites;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class MapWallPlanTests
{
    private const int GridColumns = 20;
    private const int GridRows = 10;
    private static readonly Point Scroll = new(10, 10);

    private static readonly EntityVisualView Face = Visual(16, 16);
    private static readonly EntityVisualView TwoRowFace = Visual(16, 32, pixelsPerTile: 16);
    private static readonly EntityVisualView Top = Visual(16, 16);

    private static EntityVisualView Visual(int width, int height, int? pixelsPerTile = null) =>
        new(new SpriteView("sheet.png", new Rectangle(0, 0, width, height), pixelsPerTile is { } tilePixels ? new SpritePlacement(tilePixels, width / 2, height) : null), string.Empty, default, IsDead: false);

    private static MapWallPlan Plan(FakeMapView map, Point? scroll = null)
    {
        var plan = new MapWallPlan();
        plan.Plan(map, scroll ?? Scroll, GridColumns, GridRows, mapLayer: 0);
        return plan;
    }

    private static MapOverhang Piece(int wallX, int wallY, int anchorY, EntityVisualView visual, bool isTop) =>
        new(wallX, wallY, wallX, anchorY, visual, isTop);

    [TestMethod]
    public void Plan_AWallUnderOpenFloor_HasItsTopLive_AndItsOneRowFaceCached()
    {
        var map = new FakeMapView();
        map.AddWall(15, 15, Face, Top);

        var plan = Plan(map);

        CollectionAssert.AreEqual(new[] { Piece(15, 15, 15, Face, isTop: false) }, plan.CachedPieces.ToArray());
        CollectionAssert.AreEqual(new[] { Piece(15, 15, 14, Top, isTop: true) }, plan.Overhangs.ToArray());
    }

    [TestMethod]
    public void Plan_AThickWall_ShowsTopsWithOneFaceRow_TheInnerTopCached()
    {
        var map = new FakeMapView();
        map.AddWall(15, 14, Face, Top);
        map.AddWall(15, 15, Face, Top);

        var plan = Plan(map);

        CollectionAssert.AreEqual(new[] { Piece(15, 15, 15, Face, isTop: false), Piece(15, 15, 14, Top, isTop: true) }, plan.CachedPieces.ToArray());
        CollectionAssert.AreEqual(new[] { Piece(15, 14, 13, Top, isTop: true) }, plan.Overhangs.ToArray());
    }

    [TestMethod]
    public void Plan_AWallUnderAnOffMapTile_HasItsTopCached()
    {
        var map = new FakeMapView();
        map.AddWall(5, 0, Face, Top);

        var plan = Plan(map, new Point(0, 0));

        Assert.IsEmpty(plan.Overhangs);
        CollectionAssert.Contains(plan.CachedPieces.ToArray(), Piece(5, 0, -1, Top, isTop: true));
    }

    [TestMethod]
    public void Plan_ATwoRowFaceWithOpenFloorAbove_IsLive_AndItsTopIsRaisedAboveIt()
    {
        var map = new FakeMapView();
        map.AddWall(15, 15, TwoRowFace, Top, raisedRows: 2);

        var plan = Plan(map);

        Assert.IsEmpty(plan.CachedPieces);
        CollectionAssert.AreEqual(new[] { Piece(15, 15, 15, TwoRowFace, isTop: false), Piece(15, 15, 13, Top, isTop: true) }, plan.Overhangs.ToArray());
    }

    [TestMethod]
    public void Plan_ATwoRowFaceUnderAnotherWall_IsCached()
    {
        var map = new FakeMapView();
        map.AddWall(15, 14, TwoRowFace, Top, raisedRows: 2);
        map.AddWall(15, 15, TwoRowFace, Top, raisedRows: 2);

        var plan = Plan(map);

        CollectionAssert.Contains(plan.CachedPieces.ToArray(), Piece(15, 15, 15, TwoRowFace, isTop: false));
    }

    [TestMethod]
    public void Plan_ATopIsRaisedByItsFacesHeight_WhetherOrNotItsTileDrawsAFace()
    {
        var map = new FakeMapView();
        map.AddWall(15, 14, TwoRowFace, Top, raisedRows: 2);
        map.AddWall(15, 15, TwoRowFace, Top, raisedRows: 2);

        var tops = Plan(map) is var plan ? plan.CachedPieces.Concat(plan.Overhangs).Where(piece => piece.IsTop).ToList() : [];

        CollectionAssert.AreEquivalent(new[] { Piece(15, 14, 12, Top, isTop: true), Piece(15, 15, 13, Top, isTop: true) }, tops);
    }

    [TestMethod]
    public void Plan_AWallWithNoTop_DrawsItsFaceOnEveryTile()
    {
        var map = new FakeMapView();
        map.AddWall(15, 14, Face);
        map.AddWall(15, 15, Face);

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
            map.AddWall(x, 14, Face, Top);
            map.AddWall(x, 15, Face, Top);
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
        map.AddWall(18, 15, TwoRowFace, Top, raisedRows: 2);
        map.AddWall(12, 15, TwoRowFace, Top, raisedRows: 2);
        map.AddWall(14, 12, Face, Top);

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
        map.AddWall(15, wallY, Face, Top, raisedRows: 2);

        var plan = Plan(map);

        Assert.AreEqual(expected, plan.Overhangs.Concat(plan.CachedPieces).Any(piece => piece.WallY == wallY));
    }
}
