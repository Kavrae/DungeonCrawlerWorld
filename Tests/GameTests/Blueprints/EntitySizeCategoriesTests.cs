using Engine.Math;
using Game.Blueprints;
using Game.Modules.Core.Components;

namespace Tests.GameTests.Blueprints;

[TestClass]
public sealed class EntitySizeCategoriesTests
{
    [TestMethod]
    [DataRow(1, 1, EntitySizeCategory.Normal)]
    [DataRow(1, 2, EntitySizeCategory.Large)]
    [DataRow(2, 1, EntitySizeCategory.Large)]
    [DataRow(2, 2, EntitySizeCategory.Large)]
    [DataRow(1, 3, EntitySizeCategory.Huge)]
    [DataRow(2, 3, EntitySizeCategory.Huge)]
    [DataRow(3, 3, EntitySizeCategory.Huge)]
    public void Of_NamesTheFootprintsLargestDimension(int columns, int rows, EntitySizeCategory expected) =>
        Assert.AreEqual(expected, EntitySizeCategories.Of(new Vector2Byte((byte)columns, (byte)rows), NonBlockingKind.None));

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(3, 3)]
    public void Of_TinyWhateverItsFootprint(int columns, int rows) =>
        Assert.AreEqual(EntitySizeCategory.Tiny, EntitySizeCategories.Of(new Vector2Byte((byte)columns, (byte)rows), NonBlockingKind.Tiny | NonBlockingKind.Phasing));

    [TestMethod]
    public void Of_PhasingAloneIsNotTiny() =>
        Assert.AreEqual(EntitySizeCategory.Normal, EntitySizeCategories.Of(new Vector2Byte(1, 1), NonBlockingKind.Phasing));

    [TestMethod]
    public void Of_TheLargestFootprintIsHuge() =>
        Assert.AreEqual(EntitySizeCategory.Huge, EntitySizeCategories.Of(new Vector2Byte(ResolvedBlueprint.MaximumFootprintTiles, ResolvedBlueprint.MaximumFootprintTiles), NonBlockingKind.None));
}
