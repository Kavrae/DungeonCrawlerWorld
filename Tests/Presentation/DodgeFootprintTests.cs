using Engine.Math;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class DodgeFootprintTests
{
    private static readonly Vector3Int FootprintPosition = new(5, 5, 0);
    private static readonly Vector2Byte HugeFootprint = new(3, 3);
    private static readonly Vector2Byte LargeFootprint = new(2, 2);

    [TestMethod]
    [DataRow(8, 6, 1, 0)]
    [DataRow(5, 4, 0, -1)]
    [DataRow(4, 7, -1, 0)]
    [DataRow(7, 8, 0, 1)]
    [DataRow(4, 4, -1, -1)]
    [DataRow(8, 8, 1, 1)]
    public void StepDirection_TileOutsideAHugeFootprint_StepsOneTileTowardIt(int tileX, int tileY, int stepX, int stepY) =>
        Assert.AreEqual(new Vector3Int(stepX, stepY, 0), DodgeFootprint.StepDirection(new Vector3Int(tileX, tileY, 0), FootprintPosition, HugeFootprint));

    [TestMethod]
    [DataRow(5, 5)]
    [DataRow(6, 6)]
    [DataRow(7, 7)]
    public void StepDirection_TileInsideTheFootprint_IsADodgeInPlace(int tileX, int tileY) =>
        Assert.IsNull(DodgeFootprint.StepDirection(new Vector3Int(tileX, tileY, 0), FootprintPosition, HugeFootprint));

    [TestMethod]
    public void SideTile_HugeFootprint_IsTheMiddleOfEachSide()
    {
        Assert.AreEqual(new Vector3Int(6, 4, 0), DodgeFootprint.SideTile(new Vector3Int(0, -1, 0), FootprintPosition, HugeFootprint));
        Assert.AreEqual(new Vector3Int(6, 8, 0), DodgeFootprint.SideTile(new Vector3Int(0, 1, 0), FootprintPosition, HugeFootprint));
        Assert.AreEqual(new Vector3Int(4, 6, 0), DodgeFootprint.SideTile(new Vector3Int(-1, 0, 0), FootprintPosition, HugeFootprint));
        Assert.AreEqual(new Vector3Int(8, 6, 0), DodgeFootprint.SideTile(new Vector3Int(1, 0, 0), FootprintPosition, HugeFootprint));
    }

    [TestMethod]
    public void SideTile_EvenFootprint_IsTheFirstOfTheTwoMiddles()
    {
        Assert.AreEqual(new Vector3Int(5, 4, 0), DodgeFootprint.SideTile(new Vector3Int(0, -1, 0), FootprintPosition, LargeFootprint));
        Assert.AreEqual(new Vector3Int(7, 5, 0), DodgeFootprint.SideTile(new Vector3Int(1, 0, 0), FootprintPosition, LargeFootprint));
    }

    [TestMethod]
    public void EverySideTile_StepsInItsKeysDirection()
    {
        foreach (var footprint in new[] { new Vector2Byte(1, 1), LargeFootprint, HugeFootprint })
        {
            foreach (var (_, _, direction) in DodgeFootprint.DirectionalKeys)
            {
                var sideTile = DodgeFootprint.SideTile(direction, FootprintPosition, footprint);
                Assert.AreEqual(direction, DodgeFootprint.StepDirection(sideTile, FootprintPosition, footprint));
            }
        }
    }
}
