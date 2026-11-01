using Engine.Math;
using Engine.Tiles;

namespace Tests.Math;

[TestClass]
public sealed class PathTracerTests
{
    private const ushort Every = ushort.MaxValue;
    private static readonly MapBounds Bounds = new(0, 0, 100, 100, 1);
    private static readonly Vector2Byte SingleTile = new(1, 1);
    private static readonly Vector3Int Caster = new(10, 10, 0);

    private sealed class Walls(params (int X, int Y)[] tiles) : ISightBlockers
    {
        private readonly HashSet<(int X, int Y)> _tiles = [.. tiles];

        public int Version => 0;

        public bool BlocksSight(int x, int y, int z) => _tiles.Contains((x, y));
    }

    private sealed class Occupants(params (int X, int Y, int EntityId)[] occupants) : IPathOccupants
    {
        public int EntityMetAt(Vector3Int tile)
        {
            foreach (var (x, y, entityId) in occupants)
            {
                if (x == tile.X && y == tile.Y)
                {
                    return entityId;
                }
            }

            return -1;
        }
    }

    private static TargetPathRequest Projectile(Vector3Int aim, ushort hitCount, int maximumRange = 10, ProjectilePathEnd pathEnd = ProjectilePathEnd.AtAimedTile,
        PathWallRule pathWalls = PathWallRule.StopsAtWall, byte bounces = 0, int minimumRange = 0) =>
        new(TargetPath.Projectile, pathEnd, hitCount, pathWalls, bounces, Caster, SingleTile, aim, new TargetRanges(maximumRange, MinimumRange: minimumRange));

    private static (PathTrace Trace, List<Vector3Int> Tiles, List<int> Met) Run(in TargetPathRequest request, ISightBlockers? walls = null, IPathOccupants? occupants = null)
    {
        var tiles = new List<Vector3Int>();
        var met = new List<int>();
        var trace = PathTracer.Trace(in request, walls, Bounds, occupants, tiles, met);
        return (trace, tiles, met);
    }

    private static Vector3Int Tile(int x, int y) => new(x, y, 0);

    [TestMethod]
    public void ALobbedPath_LandsOnTheAimedTile_WhateverItsPathEnd()
    {
        var request = new TargetPathRequest(TargetPath.Lobbed, ProjectilePathEnd.AtMaximumRange, 0, PathWallRule.StopsAtWall, 0, Caster, SingleTile, Tile(14, 10), new TargetRanges(10));

        var (trace, _, _) = Run(request);

        Assert.AreEqual(new PathTrace(Tile(14, 10), PathStopReason.AimedTile), trace);
    }

    [TestMethod]
    public void ALargeCasterWhoseNearestTileCantSee_FliesItsRangeFromTheFootprint()
    {
        var request = new TargetPathRequest(TargetPath.Projectile, ProjectilePathEnd.AtAimedTile, 0, PathWallRule.StopsAtWall, 0, Caster, new Vector2Byte(3, 3), Tile(14, 14), new TargetRanges(3));

        var (trace, _, _) = Run(request, new Walls((13, 13)));

        Assert.AreEqual(new PathTrace(Tile(14, 14), PathStopReason.AimedTile), trace);
    }

    [TestMethod]
    public void AppendTilesPassed_AddsEachTileOnTheWayOnceAfterThoseAlreadyThere()
    {
        var tiles = new List<Vector3Int> { Tile(12, 10) };

        PathTracer.AppendTilesPassed(Projectile(default, hitCount: 1), [Tile(14, 10), Tile(13, 10)], new Walls(), Bounds, tiles);

        CollectionAssert.AreEqual(new[] { Tile(12, 10), Tile(11, 10), Tile(13, 10), Tile(14, 10) }, tiles);
    }

    [TestMethod]
    public void AppendTilesPassed_LeavesOutTilesNearerThanMinimumRange()
    {
        var tiles = new List<Vector3Int>();

        PathTracer.AppendTilesPassed(Projectile(default, hitCount: 1, minimumRange: 3), [Tile(15, 10)], new Walls(), Bounds, tiles);

        CollectionAssert.AreEqual(new[] { Tile(13, 10), Tile(14, 10), Tile(15, 10) }, tiles);
    }

    [TestMethod]
    public void AppendTilesPassed_ALobbedPath_AddsNothing()
    {
        var tiles = new List<Vector3Int>();
        var lobbed = new TargetPathRequest(TargetPath.Lobbed, ProjectilePathEnd.AtAimedTile, 0, PathWallRule.StopsAtWall, 0, Caster, SingleTile, default, new TargetRanges(10));

        PathTracer.AppendTilesPassed(lobbed, [Tile(15, 10)], new Walls(), Bounds, tiles);

        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    [DataRow(1, PathWallRule.StopsAtWall, (byte)0)]
    [DataRow(2, PathWallRule.BouncesOffWalls, (byte)1)]
    [DataRow(3, PathWallRule.StopsAtWall, (byte)0)]
    public void ReachedTiles_HoldEveryTileAPathAimedAtAnAimableTileCrosses(int seed, PathWallRule pathWalls, byte bounces)
    {
        var random = new Random(seed);
        var walls = new Walls([.. Enumerable.Range(0, 60).Select(_ => (random.Next(0, 21), random.Next(0, 21))).Where(tile => tile != (10, 10))]);
        var request = Projectile(default, Every, maximumRange: 8, pathEnd: ProjectilePathEnd.AtMaximumRange, pathWalls: pathWalls, bounces: bounces);
        var aimable = new List<Vector3Int>();
        TargetShapeResolver.ResolveReachable(TargetShape.SingleTarget, TargetAnchor.PathEnd, Caster, SingleTile, request.Ranges, Bounds, aimable, new TargetLineOfSight(walls), TargetPath.Projectile, pathWalls);
        var reached = new List<Vector3Int>(aimable);

        PathTracer.AppendTilesPassed(request, [.. aimable], walls, Bounds, reached);

        var reachedSet = reached.ToHashSet();
        Assert.HasCount(reached.Count, reachedSet, "Each tile once.");
        foreach (var aimedTile in aimable)
        {
            var (_, crossed, _) = Run(request with { AimedTile = aimedTile }, walls);
            foreach (var tile in crossed)
            {
                Assert.Contains(tile, reachedSet, $"Aimed at {aimedTile}, the path crosses {tile}.");
            }
        }
    }

    [TestMethod]
    [DataRow(4, 0.15)]
    [DataRow(5, 0.3)]
    [DataRow(6, 0.45)]
    public void APathAimedAtAnAimableTile_GetsThere_CrossingOnlyAimableTiles(int seed, double density)
    {
        var random = new Random(seed);
        var walls = new Walls([.. Enumerable.Range(0, (int)(441 * density)).Select(_ => (random.Next(0, 21), random.Next(0, 21))).Where(tile => tile != (10, 10))]);
        var ranges = new TargetRanges(9);
        var aimable = new List<Vector3Int>();
        TargetShapeResolver.ResolveReachable(TargetShape.SingleTarget, TargetAnchor.PathEnd, Caster, SingleTile, ranges, Bounds, aimable, new TargetLineOfSight(walls), TargetPath.Projectile, PathWallRule.StopsAtWall);
        var aimableSet = aimable.ToHashSet();

        foreach (var aimedTile in aimable.Where(tile => tile != Caster))
        {
            var (toAim, _, _) = Run(Projectile(aimedTile, hitCount: 0, maximumRange: 9), walls);
            var (_, crossed, _) = Run(Projectile(aimedTile, hitCount: 0, maximumRange: 9, pathEnd: ProjectilePathEnd.AtMaximumRange), walls);

            Assert.AreEqual(PathStopReason.AimedTile, toAim.StopReason, $"Aimed at {aimedTile}.");
            Assert.IsTrue(crossed.All(aimableSet.Contains), $"Aimed at {aimedTile}, the path crosses a tile that can't be aimed at.");
        }
    }

    [TestMethod]
    public void HitCountOne_StopsOnTheFirstEntityItMeets()
    {
        var (trace, tiles, met) = Run(Projectile(Tile(16, 10), hitCount: 1), occupants: new Occupants((12, 10, 7), (14, 10, 8)));

        Assert.AreEqual(Tile(12, 10), trace.EndTile);
        Assert.AreEqual(PathStopReason.Entity, trace.StopReason);
        CollectionAssert.AreEqual(new[] { 7 }, met);
        CollectionAssert.AreEqual(new[] { Tile(11, 10), Tile(12, 10) }, tiles);
    }

    [TestMethod]
    public void HitCountTwo_StopsOnTheSecond()
    {
        var (trace, _, met) = Run(Projectile(Tile(16, 10), hitCount: 2), occupants: new Occupants((12, 10, 7), (14, 10, 8), (15, 10, 9)));

        Assert.AreEqual(Tile(14, 10), trace.EndTile);
        CollectionAssert.AreEqual(new[] { 7, 8 }, met);
    }

    [TestMethod]
    public void HitCountZero_FliesPastEveryone_ToTheAim()
    {
        var (trace, _, met) = Run(Projectile(Tile(16, 10), hitCount: 0), occupants: new Occupants((12, 10, 7)));

        Assert.AreEqual(Tile(16, 10), trace.EndTile);
        Assert.AreEqual(PathStopReason.AimedTile, trace.StopReason);
        Assert.IsEmpty(met);
    }

    [TestMethod]
    public void EveryEntity_ToMaximumRange_HitsAllAndFliesOnPastTheAim()
    {
        var (trace, tiles, met) = Run(Projectile(Tile(12, 10), Every, maximumRange: 6, pathEnd: ProjectilePathEnd.AtMaximumRange), occupants: new Occupants((12, 10, 7), (14, 10, 8), (16, 10, 9), (17, 10, 10)));

        Assert.AreEqual(Tile(16, 10), trace.EndTile);
        Assert.AreEqual(PathStopReason.MaximumRange, trace.StopReason);
        CollectionAssert.AreEqual(new[] { 7, 8, 9 }, met);
        Assert.HasCount(6, tiles);
    }

    [TestMethod]
    [DataRow(18, 10)]
    [DataRow(17, 13)]
    [DataRow(12, 19)]
    [DataRow(4, 5)]
    public void ToMaximumRange_IsTheSameLineTheCasterAnchoredLineShapeCovers(int aimX, int aimY)
    {
        var aim = Tile(aimX, aimY);
        var (_, pathTiles, _) = Run(Projectile(aim, Every, maximumRange: 8, pathEnd: ProjectilePathEnd.AtMaximumRange));
        var lineTiles = new List<Vector3Int>();

        TargetShapeResolver.Resolve(TargetShape.Line, TargetAnchor.Caster, Caster, SingleTile, aim, new TargetRanges(8, AreaSize: 8), Bounds, lineTiles);

        CollectionAssert.AreEqual(lineTiles, pathTiles);
    }

    [TestMethod]
    public void AWall_StopsThePathOnTheLastOpenTileBeforeIt()
    {
        var (trace, tiles, _) = Run(Projectile(Tile(16, 10), hitCount: 0), new Walls((13, 10)));

        Assert.AreEqual(Tile(12, 10), trace.EndTile);
        Assert.AreEqual(PathStopReason.Wall, trace.StopReason);
        CollectionAssert.DoesNotContain(tiles, Tile(13, 10));
    }

    [TestMethod]
    public void PassesThroughWalls_GoesOnToTheAim()
    {
        var (trace, _, _) = Run(Projectile(Tile(16, 10), hitCount: 0, pathWalls: PathWallRule.PassesThroughWalls), new Walls((13, 10)));

        Assert.AreEqual(Tile(16, 10), trace.EndTile);
    }

    [TestMethod]
    public void ADiagonalGapBetweenTwoWallsTouchingAtTheirCorners_StopsThePath()
    {
        var (trace, _, _) = Run(Projectile(Tile(14, 14), hitCount: 0), new Walls((12, 11), (11, 12)));

        Assert.AreEqual(Tile(11, 11), trace.EndTile);
        Assert.AreEqual(PathStopReason.Wall, trace.StopReason);
    }

    [TestMethod]
    public void BouncesOffWalls_ReflectsAndFliesTheRestOfItsRangeBack()
    {
        var (trace, tiles, met) = Run(Projectile(Tile(16, 10), Every, maximumRange: 8, pathEnd: ProjectilePathEnd.AtMaximumRange, pathWalls: PathWallRule.BouncesOffWalls, bounces: 1),
            new Walls((13, 10)), new Occupants((11, 10, 7)));

        Assert.AreEqual(PathStopReason.MaximumRange, trace.StopReason);
        Assert.IsLessThan(10, trace.EndTile.X);
        CollectionAssert.Contains(tiles, Tile(12, 10));
        CollectionAssert.DoesNotContain(tiles, Tile(13, 10));
        CollectionAssert.AreEqual(new[] { 7 }, met);
    }

    [TestMethod]
    public void BouncesOffWalls_AfterItsLastBounce_AWallStopsIt()
    {
        var (trace, _, _) = Run(Projectile(Tile(16, 10), hitCount: 0, maximumRange: 30, pathEnd: ProjectilePathEnd.AtMaximumRange, pathWalls: PathWallRule.BouncesOffWalls, bounces: 1),
            new Walls((13, 10), (7, 10)));

        Assert.AreEqual(Tile(8, 10), trace.EndTile);
        Assert.AreEqual(PathStopReason.Wall, trace.StopReason);
    }

    [TestMethod]
    public void Lobbed_MeetsNobody_AndLandsOnTheAim()
    {
        var request = new TargetPathRequest(TargetPath.Lobbed, ProjectilePathEnd.AtAimedTile, 1, PathWallRule.StopsAtWall, 0, Caster, SingleTile, Tile(15, 10), new TargetRanges(10));
        var (trace, _, met) = Run(request, occupants: new Occupants((12, 10, 7)));

        Assert.AreEqual(Tile(15, 10), trace.EndTile);
        Assert.IsEmpty(met);
    }

    [TestMethod]
    public void Lobbed_AWallStillStopsIt()
    {
        var request = new TargetPathRequest(TargetPath.Lobbed, ProjectilePathEnd.AtAimedTile, 0, PathWallRule.StopsAtWall, 0, Caster, SingleTile, Tile(15, 10), new TargetRanges(10));
        var (trace, _, _) = Run(request, new Walls((13, 10)));

        Assert.AreEqual(Tile(12, 10), trace.EndTile);
    }

    [TestMethod]
    public void MinimumRange_NothingNearerIsMet()
    {
        var (trace, _, met) = Run(Projectile(Tile(16, 10), hitCount: 1, minimumRange: 3), occupants: new Occupants((11, 10, 7), (14, 10, 8)));

        Assert.AreEqual(Tile(14, 10), trace.EndTile);
        CollectionAssert.AreEqual(new[] { 8 }, met);
    }

    [TestMethod]
    public void ALargeCaster_IsNeverPartOfItsOwnPath()
    {
        var request = new TargetPathRequest(TargetPath.Projectile, ProjectilePathEnd.AtAimedTile, 1, PathWallRule.StopsAtWall, 0, Caster, new Vector2Byte(2, 2), Tile(15, 11), new TargetRanges(10));
        var (_, tiles, _) = Run(request);

        Assert.IsTrue(tiles.All(static tile => tile.X >= 12));
        Assert.AreEqual(Tile(12, 11), tiles[0]);
    }

    [TestMethod]
    public void AnAimOnTheCaster_HasNoDirection_AndEndsWhereItStarts()
    {
        var (trace, tiles, _) = Run(Projectile(Caster, hitCount: 1));

        Assert.AreEqual(Caster, trace.EndTile);
        Assert.AreEqual(PathStopReason.NoDirection, trace.StopReason);
        Assert.IsEmpty(tiles);
    }

    [TestMethod]
    public void TryStep_OneTileAtATime_ReachesTheSameEnd()
    {
        var request = Projectile(Tile(16, 13), hitCount: 1);
        var occupants = new Occupants((14, 12, 7));
        var tracer = new PathTracer(in request, walls: null, Bounds);
        var met = new List<int>();
        var steps = 0;

        while (tracer.TryStep(occupants, met, out _))
        {
            steps++;
        }

        Assert.AreEqual(Run(request, occupants: occupants).Trace, new PathTrace(tracer.Current, tracer.StopReason));
        Assert.AreEqual(4, steps);
    }
}
