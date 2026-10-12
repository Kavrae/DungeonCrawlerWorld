using Engine.Math;
using Game.Modules.Core.Components;
using Game.World;

namespace Tests.World;

[TestClass]
public sealed class CellSeedTests
{
    private const int LayoutSeed = 0x5EED;

    private static Map MapWithLayoutLoaded(int layoutSeed)
    {
        var map = Map.Unbounded(depth: 3);
        map.LoadNeighborhood(map.CreateLayout(0, 0, layoutSeed));
        return map;
    }

    [TestMethod]
    public void GetTerrainSeed_TheSameCellAfterUnloadingAndReloadingItsLayout_IsTheSame()
    {
        var map = MapWithLayoutLoaded(LayoutSeed);
        var before = map.GetTerrainSeed(17, 40, TerrainLayer.Ground);
        var structureBefore = map.GetStructureSeed(new Vector3Int(17, 40, 1));

        map.UnloadNeighborhood(0, 0);
        map.LoadNeighborhood(map.CreateLayout(0, 0, LayoutSeed));

        Assert.AreEqual(before, map.GetTerrainSeed(17, 40, TerrainLayer.Ground));
        Assert.AreEqual(structureBefore, map.GetStructureSeed(new Vector3Int(17, 40, 1)));
    }

    [TestMethod]
    public void LoadNeighborhood_ALayoutCopiedIntoALoadedNeighborhood_BringsItsSeed()
    {
        var map = Map.Unbounded(depth: 3);
        map.LoadNeighborhood(0, 0);
        var emptySeed = map.GetTerrainSeed(17, 40, TerrainLayer.Ground);

        map.LoadNeighborhood(map.CreateLayout(0, 0, LayoutSeed));

        Assert.AreEqual(MapWithLayoutLoaded(LayoutSeed).GetTerrainSeed(17, 40, TerrainLayer.Ground), map.GetTerrainSeed(17, 40, TerrainLayer.Ground));
        Assert.AreNotEqual(emptySeed, map.GetTerrainSeed(17, 40, TerrainLayer.Ground));
    }

    [TestMethod]
    public void Seeds_DifferByLayoutSeedPositionAndStore()
    {
        var map = MapWithLayoutLoaded(LayoutSeed);
        var seeds = new[]
        {
            map.GetTerrainSeed(17, 40, TerrainLayer.Ground),
            map.GetTerrainSeed(18, 40, TerrainLayer.Ground),
            map.GetTerrainSeed(17, 41, TerrainLayer.Ground),
            map.GetTerrainSeed(17, 40, TerrainLayer.UnderGround),
            map.GetStructureSeed(new Vector3Int(17, 40, 1)),
            map.GetStructureSeed(new Vector3Int(17, 40, 2)),
            MapWithLayoutLoaded(LayoutSeed + 1).GetTerrainSeed(17, 40, TerrainLayer.Ground),
        };

        Assert.HasCount(seeds.Length, seeds.Distinct());
    }

    [TestMethod]
    public void GetTerrainSeed_AnUnloadedCell_IsZero()
    {
        var map = MapWithLayoutLoaded(LayoutSeed);

        Assert.AreEqual(0u, map.GetTerrainSeed(5_000, 5_000, TerrainLayer.Ground));
        Assert.AreEqual(0u, map.GetStructureSeed(new Vector3Int(5_000, 5_000, 1)));
    }

    /// <summary>A neighbourhood's seeds are a pure function of its seed and positions -- the same in every process -- so a seeded world draws the same tiles everywhere.</summary>
    [TestMethod]
    public void ForTerrain_IsStableAcrossRuns()
    {
        Assert.AreEqual(CellSeeds.ForTerrain(LayoutSeed, -3, 9, TerrainLayer.Ground), CellSeeds.ForTerrain(LayoutSeed, -3, 9, TerrainLayer.Ground));
        Assert.AreNotEqual(CellSeeds.ForTerrain(LayoutSeed, -3, 9, TerrainLayer.Ground), CellSeeds.ForTerrain(LayoutSeed, 3, -9, TerrainLayer.Ground));
    }

    [TestMethod]
    public void Seeds_ReducedToAFewVariants_SpreadOverEveryVariant()
    {
        var map = MapWithLayoutLoaded(LayoutSeed);
        var counts = new int[4];

        for (var x = 0; x < 64; x++)
        {
            for (var y = 0; y < 64; y++)
            {
                counts[map.GetTerrainSeed(x, y, TerrainLayer.Ground) % 4]++;
            }
        }

        foreach (var count in counts)
        {
            Assert.IsGreaterThan(64 * 64 / 4 * 0.9, count);
        }
    }
}
