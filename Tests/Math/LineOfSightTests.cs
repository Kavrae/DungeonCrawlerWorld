using Engine.Math;
using Engine.Tiles;

namespace Tests.Math;

[TestClass]
public sealed class LineOfSightTests
{
    private sealed class Walls(params (int X, int Y)[] tiles) : ISightBlockers
    {
        public HashSet<(int X, int Y)> Tiles { get; } = [.. tiles];

        public int Version => 0;

        public bool BlocksSight(int x, int y, int z) => Tiles.Contains((x, y));
    }

    private static Vector3Int Tile(int x, int y) => new(x, y, 0);

    private static Walls RandomWalls(int seed, int extent, double density)
    {
        var random = new Random(seed);
        var walls = new Walls();
        for (var x = -extent; x <= extent; x++)
        {
            for (var y = -extent; y <= extent; y++)
            {
                if ((x != 0 || y != 0) && random.NextDouble() < density)
                {
                    walls.Tiles.Add((x, y));
                }
            }
        }

        return walls;
    }

    [TestMethod]
    public void OpenGround_SeesEverything()
    {
        var visible = new HashSet<Vector3Int>();

        LineOfSight.ComputeVisible(Tile(0, 0), 5, new Walls(), visible);

        Assert.HasCount(11 * 11, visible);
    }

    [TestMethod]
    public void AWallStraightAhead_HidesTheTileBehindIt()
    {
        var walls = new Walls((2, 0));

        Assert.IsTrue(LineOfSight.IsVisible(Tile(0, 0), Tile(2, 0), walls));
        Assert.IsFalse(LineOfSight.IsVisible(Tile(0, 0), Tile(3, 0), walls));
        Assert.IsFalse(LineOfSight.IsVisible(Tile(0, 0), Tile(6, 0), walls));
    }

    [TestMethod]
    public void APillar_HidesAWedgeBehindIt_NotTheRoom()
    {
        var walls = new Walls((2, 0));

        Assert.IsFalse(LineOfSight.IsVisible(Tile(0, 0), Tile(4, 0), walls));
        Assert.IsTrue(LineOfSight.IsVisible(Tile(0, 0), Tile(4, 2), walls));
        Assert.IsTrue(LineOfSight.IsVisible(Tile(0, 0), Tile(4, -2), walls));
    }

    [TestMethod]
    public void TwoWallsTouchingAtTheirCorners_BlockSightBetweenThem()
    {
        var walls = new Walls((1, 0), (0, 1));

        Assert.IsFalse(LineOfSight.IsVisible(Tile(0, 0), Tile(1, 1), walls));
        Assert.IsFalse(LineOfSight.IsVisible(Tile(0, 0), Tile(3, 3), walls));
        Assert.IsFalse(LineOfSight.IsVisible(Tile(1, 1), Tile(0, 0), walls));
    }

    [TestMethod]
    public void OneWallBesideADiagonal_DoesNotBlockIt()
    {
        var walls = new Walls((1, 0));

        Assert.IsTrue(LineOfSight.IsVisible(Tile(0, 0), Tile(3, 3), walls));
    }

    [TestMethod]
    public void AWallAtEitherEnd_DoesNotHideItself()
    {
        var walls = new Walls((0, 0), (4, 1));

        Assert.IsTrue(LineOfSight.IsVisible(Tile(0, 0), Tile(4, 1), walls));
    }

    [TestMethod]
    public void DownACorridor_TheWholeLengthIsVisible()
    {
        var walls = new Walls();
        for (var x = -1; x <= 10; x++)
        {
            walls.Tiles.Add((x, -1));
            walls.Tiles.Add((x, 1));
        }

        for (var x = 1; x <= 10; x++)
        {
            Assert.IsTrue(LineOfSight.IsVisible(Tile(0, 0), Tile(x, 0), walls));
        }

        Assert.IsFalse(LineOfSight.IsVisible(Tile(0, 0), Tile(6, 2), walls));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    public void IsVisible_IsSymmetric(int seed)
    {
        var walls = RandomWalls(seed, 8, 0.3);
        for (var x = -8; x <= 8; x++)
        {
            for (var y = -8; y <= 8; y++)
            {
                var from = Tile(x, y);
                foreach (var to in new[] { Tile(0, 0), Tile(3, -5), Tile(-7, 2) })
                {
                    Assert.AreEqual(LineOfSight.IsVisible(from, to, walls), LineOfSight.IsVisible(to, from, walls), $"{from} and {to}");
                }
            }
        }
    }

    [TestMethod]
    [DataRow(1, 0.15)]
    [DataRow(2, 0.3)]
    [DataRow(3, 0.45)]
    [DataRow(4, 0.3)]
    [DataRow(5, 0.2)]
    public void ComputeVisible_AgreesWithIsVisibleForEveryTile(int seed, double density)
    {
        const int Radius = 9;
        var walls = RandomWalls(seed, Radius + 1, density);
        var visible = new HashSet<Vector3Int>();

        LineOfSight.ComputeVisible(Tile(0, 0), Radius, walls, visible);

        for (var x = -Radius; x <= Radius; x++)
        {
            for (var y = -Radius; y <= Radius; y++)
            {
                var tile = Tile(x, y);
                Assert.AreEqual(LineOfSight.IsVisible(Tile(0, 0), tile, walls), visible.Contains(tile), $"{tile}");
            }
        }
    }

    [TestMethod]
    public void IsVisibleFromFootprint_SeesWhatAnyFootprintTileSees()
    {
        var walls = new Walls((2, 0), (2, 1));

        Assert.IsFalse(LineOfSight.IsVisible(Tile(0, 0), Tile(4, 0), walls));
        Assert.IsFalse(LineOfSight.IsVisibleFromFootprint(Tile(0, 0), new Vector2Byte(1, 3), Tile(4, 0), walls));
        Assert.IsTrue(LineOfSight.IsVisibleFromFootprint(Tile(0, 0), new Vector2Byte(1, 4), Tile(4, 0), walls));
    }

    [TestMethod]
    public void ComputeVisible_WallsCuttingTheSameLinesExactlyAtACorner_KeepTheLinesBetweenThem()
    {
        var walls = new Walls((0, 2), (2, 2), (2, 3), (0, 4), (3, 6), (0, 7), (8, 7));
        var visible = new HashSet<Vector3Int>();

        LineOfSight.ComputeVisible(Tile(0, 0), 10, walls, visible);

        Assert.IsTrue(LineOfSight.IsVisible(Tile(0, 0), Tile(2, 7), walls));
        Assert.Contains(Tile(1, 7), visible);
        Assert.Contains(Tile(2, 7), visible);
        Assert.Contains(Tile(3, 7), visible);
    }

    [TestMethod]
    public void ADigitalLineThatStepsAroundAWall_Sees()
    {
        var walls = new Walls((2, 0));

        Assert.IsFalse(LineOfSight.IsVisible(Tile(0, 0), Tile(4, 0), walls), "Every line along the row crosses the wall.");
        Assert.IsTrue(LineOfSight.IsVisible(Tile(0, 1), Tile(4, 0), walls), "The line that keeps to row 1 until its last step passes beside it.");
        Assert.IsTrue(LineOfSight.IsVisible(Tile(4, 0), Tile(0, 1), walls), "And back again.");
    }
}
