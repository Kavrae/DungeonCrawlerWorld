using Engine.Math;
using Game.Modules.Core.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.MapRendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class MapDrawOrderTests
{
    private const int GridColumns = 20;
    private const int GridRows = 10;
    private static readonly Point Scroll = new(10, 10);

    private static List<MapDrawItem> Plan(FakeMapView map, int mapLayer = 0, int layerCount = 1, IReadOnlyList<MapOverhang>? overhangs = null)
    {
        var drawOrder = new MapDrawOrder();
        drawOrder.Plan(map, Scroll, GridColumns, GridRows, mapLayer, layerCount, overhangs ?? []);
        return [.. drawOrder.Items];
    }

    private static int IndexOf(List<MapDrawItem> items, int entityId, MapDrawItemKind kind) =>
        items.FindIndex(item => item.EntityId == entityId && item.Kind == kind);

    [TestMethod]
    public void Plan_ASouthNeighbour_ComesAfterANorthOne_EvenOneColumnToTheLeft()
    {
        var map = new FakeMapView();
        var north = map.AddBlocking(15, 13);
        var southWest = map.AddBlocking(14, 14);

        var items = Plan(map);

        Assert.IsGreaterThan(IndexOf(items, north, MapDrawItemKind.Blocking), IndexOf(items, southWest, MapDrawItemKind.Blocking));
    }

    [TestMethod]
    public void Plan_ATwoByTwoBlockingOccupant_YieldsOnceWithItsWholeFootprint_InItsBottomRow()
    {
        var map = new FakeMapView();
        var large = map.AddBlocking(13, 13, new Vector2Byte(2, 2));
        var eastInTopRow = map.AddBlocking(16, 13);

        var items = Plan(map);

        var largeItems = items.Where(item => item.EntityId == large).ToList();
        Assert.HasCount(1, largeItems);
        Assert.AreEqual(new MapDrawItem(MapDrawItemKind.Blocking, large, 13, 13, new Vector2Byte(2, 2)), largeItems[0]);
        Assert.IsGreaterThan(IndexOf(items, eastInTopRow, MapDrawItemKind.Blocking), IndexOf(items, large, MapDrawItemKind.Blocking));
    }

    [TestMethod]
    [DataRow(15, 21, true)]
    [DataRow(15, 22, false)]
    [DataRow(8, 12, true)]
    [DataRow(7, 12, false)]
    [DataRow(31, 12, true)]
    [DataRow(32, 12, false)]
    public void Plan_AnEntityAnchoredOutsideTheGrid_IsYieldedOnlyWithinTheReachBoxOverscan(int x, int y, bool expected)
    {
        var map = new FakeMapView();
        var entity = map.AddBlocking(x, y);

        var items = Plan(map);

        Assert.AreEqual(expected, IndexOf(items, entity, MapDrawItemKind.Blocking) >= 0);
    }

    [TestMethod]
    public void Plan_AHugeEntityAnchoredTwoColumnsLeftOfTheGrid_IsYieldedReachingIntoView()
    {
        var map = new FakeMapView();
        var huge = map.AddBlocking(8, 12, new Vector2Byte(3, 3));

        var item = Plan(map).Single(item => item.EntityId == huge);

        Assert.AreEqual(new MapDrawItem(MapDrawItemKind.Blocking, huge, 8, 12, new Vector2Byte(3, 3)), item);
        Assert.IsGreaterThanOrEqualTo(Scroll.X, item.MapX + item.Size.X - 1);
    }

    [TestMethod]
    public void Plan_ACorpse_IsYieldedInItsTopRow_AheadOfALiveOccupantOnItsLowerTile()
    {
        var map = new FakeMapView();
        var corpse = map.AddNonBlocking(13, 13, new Vector2Byte(2, 2), NonBlockingKind.None, isDead: true);
        var standingOnCorpse = map.AddBlocking(13, 14);

        var items = Plan(map);

        Assert.AreEqual(new MapDrawItem(MapDrawItemKind.Corpse, corpse, 13, 13, new Vector2Byte(2, 2)), items.Single(item => item.EntityId == corpse));
        Assert.IsLessThan(IndexOf(items, standingOnCorpse, MapDrawItemKind.Blocking), IndexOf(items, corpse, MapDrawItemKind.Corpse));
    }

    [TestMethod]
    public void Plan_AMultiTilePhasingEntity_IsYieldedOnce_AfterTheBlockingOccupantSharingItsAnchorTile()
    {
        var map = new FakeMapView();
        var phasing = map.AddNonBlocking(13, 13, new Vector2Byte(2, 2), NonBlockingKind.Phasing);
        var blockingAtAnchor = map.AddBlocking(13, 14);

        var items = Plan(map);

        Assert.HasCount(1, items.Where(item => item.EntityId == phasing).ToList());
        Assert.IsGreaterThan(IndexOf(items, blockingAtAnchor, MapDrawItemKind.Blocking), IndexOf(items, phasing, MapDrawItemKind.Phasing));
    }

    [TestMethod]
    public void Plan_TinyOccupants_AreYieldedOnlyInsideTheGrid()
    {
        var map = new FakeMapView();
        var inside = map.AddNonBlocking(12, 12, new Vector2Byte(1, 1), NonBlockingKind.Tiny);
        var below = map.AddNonBlocking(12, 20, new Vector2Byte(1, 1), NonBlockingKind.Tiny);

        var items = Plan(map);

        Assert.IsGreaterThanOrEqualTo(0, IndexOf(items, inside, MapDrawItemKind.Tiny));
        Assert.AreEqual(-1, IndexOf(items, below, MapDrawItemKind.Tiny));
    }

    [TestMethod]
    public void Plan_LayerBadges_ComeAfterEveryOccupant_AndOnlyForTilesWithAnotherLayerOccupied()
    {
        var map = new FakeMapView();
        map.AddBlocking(12, 12);
        map.AddBlocking(25, 19);
        map.SetOccupiedLayers(11, 11, 0b011);
        map.SetOccupiedLayers(13, 11, 0b001);

        var items = Plan(map, mapLayer: 0, layerCount: 2);

        var badges = items.Where(item => item.Kind == MapDrawItemKind.LayerBadge).ToList();
        Assert.HasCount(1, badges);
        Assert.AreEqual(new MapDrawItem(MapDrawItemKind.LayerBadge, -1, 11, 11, new Vector2Byte(1, 1), HasHigherLayer: true, HasLowerLayer: false), badges[0]);
        Assert.AreEqual(items.Count - 1, items.IndexOf(badges[0]));
    }

    [TestMethod]
    public void Plan_AWallsOverhang_ComesAfterTheRowAboveAndBeforeItsOwnRowsOccupants()
    {
        var map = new FakeMapView();
        var behindTheWall = map.AddBlocking(16, 13);
        var westInTheWallsRow = map.AddBlocking(14, 14);
        var top = new MapOverhang(15, 14, 15, 13, default, IsTop: true);

        var items = Plan(map, overhangs: [top]);

        var overhangIndex = IndexOf(items, 0, MapDrawItemKind.Overhang);
        Assert.AreEqual(new MapDrawItem(MapDrawItemKind.Overhang, 0, 15, 13, new Vector2Byte(1, 1)), items[overhangIndex]);
        Assert.IsGreaterThan(IndexOf(items, behindTheWall, MapDrawItemKind.Blocking), overhangIndex);
        Assert.IsLessThan(IndexOf(items, westInTheWallsRow, MapDrawItemKind.Blocking), overhangIndex);
    }
}
