using Game.Sprites;
using Game.Terrain;

namespace Tests.Terrain;

[TestClass]
public sealed class StructureLinksTests
{
    private const int X = 10;
    private const int Y = 10;

    /// <summary>Joined to the walls listed, as offsets from (X, Y).</summary>
    private readonly struct Neighbours(params (int X, int Y)[] walls) : INeighbourLinks
    {
        public bool LinksTo(int x, int y) => walls.Contains((x - X, y - Y));
    }

    [TestMethod]
    public void CardinalMask_EachOfTheSixteenLayouts_GivesItsMask()
    {
        for (var mask = 0; mask < 16; mask++)
        {
            var walls = new List<(int, int)>();
            if ((mask & SpriteLinks.North) != 0)
            {
                walls.Add((0, -1));
            }
            if ((mask & SpriteLinks.East) != 0)
            {
                walls.Add((1, 0));
            }
            if ((mask & SpriteLinks.South) != 0)
            {
                walls.Add((0, 1));
            }
            if ((mask & SpriteLinks.West) != 0)
            {
                walls.Add((-1, 0));
            }

            Assert.AreEqual(mask, StructureLinks.CardinalMask(new Neighbours([.. walls]), X, Y), $"walls at {string.Join(", ", walls)}");
        }
    }

    [TestMethod]
    public void CardinalMask_DiagonalNeighbours_DontCount()
    {
        Assert.AreEqual(0, StructureLinks.CardinalMask(new Neighbours((1, 1), (-1, -1), (1, -1), (-1, 1)), X, Y));
    }

    [TestMethod]
    [DataRow(false, false, SpriteLinks.Standalone)]
    [DataRow(true, false, SpriteLinks.HorizontalEast)]
    [DataRow(false, true, SpriteLinks.HorizontalWest)]
    [DataRow(true, true, SpriteLinks.HorizontalEast | SpriteLinks.HorizontalWest)]
    public void HorizontalMask_LeftAndRightNeighbours_GiveStandaloneLeftEndRightEndOrMiddle(bool wallEast, bool wallWest, int expected)
    {
        var walls = new List<(int, int)> { (0, -1), (0, 1) };
        if (wallEast)
        {
            walls.Add((1, 0));
        }
        if (wallWest)
        {
            walls.Add((-1, 0));
        }

        Assert.AreEqual(expected, StructureLinks.HorizontalMask(new Neighbours([.. walls]), X, Y));
    }

    [TestMethod]
    public void MaskFor_APlainPart_HasNoMask()
    {
        Assert.IsNull(StructureLinks.MaskFor(SpriteLinking.None, new Neighbours((1, 0)), X, Y));
    }
}
