using Engine.Math;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.MapRendering;
using Presentation.Rendering;
using Presentation.UI;
using SpriteFormat;
using UIEngine.Rendering;

namespace Tests.Presentation.MapRendering;

[TestClass]
public sealed class OverhangFadeTests
{
    private const int Tile = 32;
    private const int PlayerId = 7;
    private const int InspectedId = 12;

    private static Rectangle TileRectangle(int column, int row) => new(column * Tile, row * Tile, Tile, Tile);

    [TestMethod]
    public void AlphaFor_AnOverhangOverlappingTheWatchedSprite_Fades_AndOneBesideIt_DoesNot()
    {
        List<Rectangle> watched = [TileRectangle(5, 5)];

        Assert.AreEqual(OverhangFade.FadedAlpha, OverhangFade.AlphaFor(TileRectangle(5, 5), watched));
        Assert.AreEqual(1f, OverhangFade.AlphaFor(TileRectangle(6, 5), watched));
        Assert.AreEqual(1f, OverhangFade.AlphaFor(TileRectangle(5, 4), watched));
    }

    [TestMethod]
    public void WatchedRectangle_ATallPlacedSprite_CoversTheRowAboveItsTile()
    {
        var sprite = new SpriteView("sheet.png", new Rectangle(0, 0, 16, 32), new SpritePlacement(16, 8, 32));

        var watched = OverhangFade.WatchedRectangle(new Vector2(5 * Tile, 5 * Tile), new Vector2(Tile, Tile), new Vector2Byte(1, 1), sprite);

        Assert.AreEqual(OverhangFade.FadedAlpha, OverhangFade.AlphaFor(TileRectangle(5, 4), [watched]));
        Assert.AreEqual(1f, OverhangFade.AlphaFor(TileRectangle(5, 3), [watched]));
    }

    [TestMethod]
    [DataRow(InspectionMode.None, false)]
    [DataRow(InspectionMode.Basic, false)]
    [DataRow(InspectionMode.Detail, true)]
    [DataRow(InspectionMode.Admin, true)]
    public void CollectWatchedEntities_TheInspectedEntity_CountsOnlyInDetailOrAdmin(InspectionMode mode, bool watched)
    {
        var watchedEntityIds = new List<int>();

        OverhangFade.CollectWatchedEntities(watchedEntityIds, PlayerId, mode, InspectedId);

        CollectionAssert.Contains(watchedEntityIds, PlayerId);
        Assert.AreEqual(watched, watchedEntityIds.Contains(InspectedId));
    }

    [TestMethod]
    public void CollectWatchedEntities_InspectingThePlayer_WatchesThemOnce()
    {
        var watchedEntityIds = new List<int>();

        OverhangFade.CollectWatchedEntities(watchedEntityIds, PlayerId, InspectionMode.Detail, PlayerId);

        CollectionAssert.AreEqual(new[] { PlayerId }, watchedEntityIds);
    }

    [TestMethod]
    public void WatchedRectangle_ATwoByTwoEntity_FadesAnOverhangOnEveryTileOfItsFootprint()
    {
        var watched = OverhangFade.WatchedRectangle(new Vector2(5 * Tile, 5 * Tile), new Vector2(2 * Tile, 2 * Tile), new Vector2Byte(2, 2), sprite: null);

        foreach (var (column, row) in new[] { (5, 5), (6, 5), (5, 6), (6, 6) })
        {
            Assert.AreEqual(OverhangFade.FadedAlpha, OverhangFade.AlphaFor(TileRectangle(column, row), [watched]), $"({column}, {row})");
        }

        Assert.AreEqual(1f, OverhangFade.AlphaFor(TileRectangle(7, 5), [watched]));
    }
}
