using Game.Terrain;
using Microsoft.Xna.Framework;
using Presentation.MapRendering;
using Presentation.Rendering;
using SpriteFormat;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class MapTerrainCellsTests
{
    private const string Stone = "stone";
    private const string Brick = "brick";
    private const string Plain = "plain";

    private static readonly Point Scroll = new(0, 0);

    /// <summary>A wall entry whose sixteen top pieces and four face pieces are each a different cell, so the piece drawn names the mask that picked it.</summary>
    private static SpriteManifestEntry LinkedWall(string name, int row) =>
        new(
            name,
            [.. Enumerable.Range(0, 4).Select(mask => (SpriteManifestCell?)new SpriteManifestCell("sheet.png", mask, row, 16, 16))],
            Linking: SpriteLinking.Horizontal,
            Top: new SpriteManifestPart([.. Enumerable.Range(0, 16).Select(mask => (SpriteManifestCell?)new SpriteManifestCell("sheet.png", mask, row + 1, 16, 16))], Linking: SpriteLinking.Cardinal));

    private static readonly SpriteManifestResolver Sprites = new(SpriteManifest.Parse(SpriteManifest.Serialize(
    [
        LinkedWall(Stone, 0),
        LinkedWall(Brick, 2),
        new SpriteManifestEntry(Plain, [new SpriteManifestCell("sheet.png", 0, 9, 16, 16)]),
    ])));

    private static MapTerrainCells Read(FakeMapView map)
    {
        var cells = new MapTerrainCells(map, Sprites);
        cells.Read(Scroll, 20, 20, mapLayer: 0);
        return cells;
    }

    private static int TopMaskDrawn(MapTerrainCells cells, int x, int y)
    {
        Assert.IsTrue(cells.TryGetTop(x, y, out var top, out _));
        return top.Sprite!.Value.SourceRectangle.X / 16;
    }

    private static int FaceMaskDrawn(MapTerrainCells cells, int x, int y)
    {
        Assert.IsTrue(cells.TryGetFace(x, y, out var face));
        return face.Sprite!.Value.SourceRectangle.X / 16;
    }

    [TestMethod]
    public void TopMasks_AWallRun_JoinEachTopToItsSameGroupNeighbours()
    {
        var map = new FakeMapView();
        map.AddWall(3, 5, Stone);
        map.AddWall(4, 5, Stone);
        map.AddWall(5, 5, Stone);
        map.AddWall(4, 6, Stone);

        var cells = Read(map);

        Assert.AreEqual(SpriteLinks.East, TopMaskDrawn(cells, 3, 5));
        Assert.AreEqual(SpriteLinks.East | SpriteLinks.West | SpriteLinks.South, TopMaskDrawn(cells, 4, 5));
        Assert.AreEqual(SpriteLinks.West, TopMaskDrawn(cells, 5, 5));
        Assert.AreEqual(SpriteLinks.North, TopMaskDrawn(cells, 4, 6));
    }

    [TestMethod]
    public void TopMasks_ACornerAndALonePillar()
    {
        var map = new FakeMapView();
        map.AddWall(10, 10, Stone);
        map.AddWall(11, 10, Stone);
        map.AddWall(10, 11, Stone);
        map.AddWall(15, 15, Stone);

        var cells = Read(map);

        Assert.AreEqual(SpriteLinks.East | SpriteLinks.South, TopMaskDrawn(cells, 10, 10));
        Assert.AreEqual(0, TopMaskDrawn(cells, 15, 15));
    }

    [TestMethod]
    public void TopMasks_AStructureInAnotherLinkGroup_DoesNotJoin()
    {
        var map = new FakeMapView();
        map.AddWall(4, 5, Stone);
        map.AddWall(5, 5, Brick);

        var cells = Read(map);

        Assert.AreEqual(0, TopMaskDrawn(cells, 4, 5));
    }

    [TestMethod]
    public void TopMasks_AnExplicitLinkGroup_JoinsDifferentSprites()
    {
        var map = new FakeMapView();
        map.AddWall(4, 5, Stone);
        map.AddWall(5, 5, Brick);
        map.Terrain.Register(map.Terrain.TryGet(map.Terrain.GetId($"test:{Stone}"), out var stone) ? stone with { LinkGroup = "masonry" } : throw new AssertFailedException());
        map.Terrain.Register(map.Terrain.TryGet(map.Terrain.GetId($"test:{Brick}"), out var brick) ? brick with { LinkGroup = "masonry" } : throw new AssertFailedException());

        var cells = Read(map);

        Assert.AreEqual(SpriteLinks.East, TopMaskDrawn(cells, 4, 5));
    }

    [TestMethod]
    public void TopMasks_AnOffMapNeighbour_Joins()
    {
        var map = new FakeMapView();
        map.AddWall(0, 5, Stone);

        var cells = Read(map);

        Assert.AreEqual(SpriteLinks.West, TopMaskDrawn(cells, 0, 5));
    }

    [TestMethod]
    public void TopMasks_AreReadFromTheWallsOwnNeighbours_NotFromTheTileItsTopIsDrawnOn()
    {
        var map = new FakeMapView();
        map.AddWall(4, 5, Stone);
        map.AddWall(4, 3, Stone);
        map.AddWall(3, 4, Stone);

        var cells = Read(map);

        Assert.AreEqual(0, TopMaskDrawn(cells, 4, 5));
    }

    [TestMethod]
    public void FaceMasks_AWallRunsFaces_AreLeftEndMiddleRightEnd_AndALoneWallsIsStandalone()
    {
        var map = new FakeMapView();
        map.AddWall(3, 5, Stone);
        map.AddWall(4, 5, Stone);
        map.AddWall(5, 5, Stone);
        map.AddWall(8, 2, Stone);

        var cells = Read(map);

        Assert.AreEqual(SpriteLinks.HorizontalEast, FaceMaskDrawn(cells, 3, 5));
        Assert.AreEqual(SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest, FaceMaskDrawn(cells, 4, 5));
        Assert.AreEqual(SpriteLinks.HorizontalWest, FaceMaskDrawn(cells, 5, 5));
        Assert.AreEqual(SpriteLinks.Standalone, FaceMaskDrawn(cells, 8, 2));
    }

    [TestMethod]
    public void AWallWithNoTop_HasNone_AndItsFaceIsItsPlainPicture()
    {
        var map = new FakeMapView();
        map.AddWall(4, 5, Plain);

        var cells = Read(map);

        Assert.IsFalse(cells.TryGetTop(4, 5, out _, out _));
        Assert.IsTrue(cells.TryGetFace(4, 5, out var face));
        Assert.AreEqual(9 * 16, face.Sprite!.Value.SourceRectangle.Y);
    }

    [TestMethod]
    public void AWallWhoseSpriteTheManifestLacks_DrawsItsGlyph()
    {
        var map = new FakeMapView();
        map.AddWall(4, 5, "not-in-the-manifest");

        var cells = Read(map);

        Assert.IsTrue(cells.TryGetFace(4, 5, out var face));
        Assert.IsNull(face.Sprite);
        Assert.AreEqual("#", face.Glyph);
        Assert.IsFalse(cells.TryGetTop(4, 5, out _, out _));
    }

    [TestMethod]
    public void ReplacingADefinition_IsReadTheNextTime()
    {
        var map = new FakeMapView();
        map.AddWall(4, 5, Plain);
        var cells = Read(map);
        Assert.IsNotNull(cells.TryGetFace(4, 5, out var before) ? before.Sprite : null);

        map.Terrain.Register(map.Terrain.TryGet(map.Terrain.GetId($"test:{Plain}"), out var plain) ? plain with { SpriteName = null } : throw new AssertFailedException());
        cells.Read(Scroll, 20, 20, mapLayer: 0);

        Assert.IsTrue(cells.TryGetFace(4, 5, out var after));
        Assert.IsNull(after.Sprite);
    }

    [TestMethod]
    public void HasStructure_AndIsOnMap_FollowTheCopiedCells()
    {
        var map = new FakeMapView();
        map.AddWall(4, 5, Stone);

        var cells = Read(map);

        Assert.IsTrue(cells.HasStructure(4, 5));
        Assert.IsFalse(cells.HasStructure(5, 5));
        Assert.IsTrue(cells.IsOnMap(0, 0));
        Assert.IsFalse(cells.IsOnMap(-1, 0));
    }
}
