using Engine.Math;
using Game.Terrain;
using Microsoft.Xna.Framework;
using Game.Sprites;

namespace Tests.Terrain;

[TestClass]
public sealed class TerrainRegistryTests
{
    private sealed class CountingRandom : Random
    {
        public int Calls { get; private set; }

        public override int Next(int minValue, int maxValue)
        {
            Calls++;
            return base.Next(minValue, maxValue);
        }
    }

    private static TerrainDefinition Definition(string key, string name = "Sand", string? spriteName = null) =>
        new(key, name, "", Color.Tan, "~", Color.White, SpriteName: spriteName);

    /// <summary>The checked-in wall entry the built-in stone wall draws with; the expectations below are read from it, so editing its art doesn't break them.</summary>
    private const string WallSpriteName = "wall-stone";

    [TestMethod]
    public void GetLinkGroup_DefinitionsDrawnWithTheSameWallEntry_ShareOne_AndOthersDont()
    {
        var registry = new TerrainRegistry();
        var stone = registry.Register(Definition("test:stone", spriteName: WallSpriteName));
        var moreStone = registry.Register(Definition("test:more-stone", spriteName: WallSpriteName));
        var dirt = registry.Register(Definition("test:dirt", spriteName: "Dirt"));
        var pillar = registry.Register(Definition("test:pillar"));
        var otherPillar = registry.Register(Definition("test:other-pillar"));

        Assert.AreEqual(registry.GetLinkGroup(stone), registry.GetLinkGroup(moreStone));
        Assert.AreNotEqual(registry.GetLinkGroup(stone), registry.GetLinkGroup(dirt));
        Assert.AreNotEqual(registry.GetLinkGroup(pillar), registry.GetLinkGroup(otherPillar));
        Assert.AreNotEqual(registry.GetLinkGroup(pillar), registry.GetLinkGroup(stone));
    }

    [TestMethod]
    public void GetLinkGroup_ReplacingADefinition_ReReadsItsGroup()
    {
        var registry = new TerrainRegistry();
        var wall = registry.Register(Definition("test:wall", spriteName: WallSpriteName));
        var stone = registry.Register(Definition("test:stone", spriteName: WallSpriteName));
        Assert.AreEqual(registry.GetLinkGroup(stone), registry.GetLinkGroup(wall));

        registry.Register(Definition("test:wall", spriteName: "Dirt"));

        Assert.AreNotEqual(registry.GetLinkGroup(stone), registry.GetLinkGroup(wall));
    }

    [TestMethod]
    public void SpriteSet_ADefinitionWithNoTop_HasNone()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:dirt", spriteName: "Dirt"));

        Assert.IsTrue(registry.TryGetSpriteSet(id, out var sprites));
        Assert.IsFalse(sprites.HasTop);
        Assert.IsFalse(sprites.TryGetTop(0, mask: null, out _));
    }

    [TestMethod]
    public void SpriteSet_AWallsTop_FollowsTheVariant_FallsBackToItsFullyJoinedPiece_RaisedByItsFacesHeight()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:wall", spriteName: WallSpriteName));
        Assert.IsTrue(SpriteManifest.TryGetEntry(WallSpriteName, out var entry));
        var top = entry.Top!;

        for (var variant = 0; variant < registry.GetVariantCount(id); variant++)
        {
            var expectedIndex = SpriteLinks.CellIndexFor(top, variant, SpriteLinks.FallbackMask(top.Linking));
            Assert.IsTrue(SpriteManifest.TryResolveTopCell(WallSpriteName, expectedIndex, out var expected));

            Assert.IsTrue(registry.TryGetSpriteSet(id, out var sprites));
            Assert.IsTrue(sprites.TryGetTop(variant, mask: null, out var sprite));
            Assert.AreEqual(expected, sprite);
            Assert.AreEqual(entry.Face.HeightRows(), sprites.TopRaisedRows);
        }
    }

    [TestMethod]
    public void GetVariantCount_AWall_CountsVariantSets_TheLargerOfFaceAndTop()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:wall", spriteName: WallSpriteName));
        Assert.IsTrue(SpriteManifest.TryGetEntry(WallSpriteName, out var entry));

        var faceSets = entry.Cells.Count / SpriteLinks.CellsPerSet(entry.Linking);
        var topSets = entry.Top!.Cells.Count / SpriteLinks.CellsPerSet(entry.Top.Linking);

        Assert.AreEqual(System.Math.Max(faceSets, topSets), registry.GetVariantCount(id));
    }

    [TestMethod]
    public void TryGetSprite_AWallFace_IsItsStandalonePieceForTheVariantsSet()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:wall", spriteName: WallSpriteName));
        Assert.IsTrue(SpriteManifest.TryGetEntry(WallSpriteName, out var entry));

        for (var variant = 0; variant < registry.GetVariantCount(id); variant++)
        {
            var expectedIndex = SpriteLinks.CellIndexFor(entry.Face, variant, SpriteLinks.FallbackMask(entry.Linking));
            Assert.IsTrue(SpriteManifest.TryResolveCell(WallSpriteName, expectedIndex, out var expected));

            Assert.IsTrue(registry.TryGetSprite(new TerrainCell(id, (byte)variant), out var sprite));
            Assert.AreEqual(expected, sprite);
        }
    }

    [TestMethod]
    public void Register_AssignsIdsFromOne_AndNoneIsNeverAValidId()
    {
        var registry = new TerrainRegistry();

        var first = registry.Register(Definition("test:a"));
        var second = registry.Register(Definition("test:b"));

        Assert.AreEqual((ushort)1, first);
        Assert.AreEqual((ushort)2, second);
        Assert.AreEqual(2, registry.Count);
        Assert.IsFalse(registry.TryGet(TerrainRegistry.None, out _));
    }

    [TestMethod]
    public void Register_SameKeyAgain_ReplacesTheDefinitionAndKeepsItsId()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:a", name: "Sand"));

        var replacedId = registry.Register(Definition("test:a", name: "Glass"));

        Assert.AreEqual(id, replacedId);
        Assert.AreEqual(1, registry.Count);
        Assert.IsTrue(registry.TryGet(id, out var definition));
        Assert.AreEqual("Glass", definition.Name);
    }

    [TestMethod]
    public void BlocksMovement_FollowsTheDefinition_IncludingAReplacement()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:a") with { BlocksMovement = true });
        Assert.IsTrue(registry.BlocksMovement(id));

        registry.Register(Definition("test:a"));

        Assert.IsFalse(registry.BlocksMovement(id));
        Assert.IsFalse(registry.BlocksMovement(TerrainRegistry.None));
        Assert.IsFalse(registry.BlocksMovement(99));
    }

    [TestMethod]
    public void GetId_UnknownKey_Throws() =>
        Assert.ThrowsExactly<KeyNotFoundException>(() => new TerrainRegistry().GetId("test:missing"));

    [TestMethod]
    public void CreateCell_GlyphOnlyTerrain_DoesNotDrawFromTheRandomSource()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:a"));
        var random = new CountingRandom();

        var cell = registry.CreateCell(id, new MathUtility(random));

        Assert.AreEqual(new TerrainCell(id, 0), cell);
        Assert.AreEqual(0, random.Calls);
        Assert.IsFalse(registry.TryGetSprite(cell, out _));
    }

    [TestMethod]
    public void CreateCell_MultiVariantSprite_RollsAVariantWithinRange()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:grass", spriteName: "Grass"));
        var variantCount = SpriteManifest.CountCells("Grass");
        Assert.IsGreaterThan(1, variantCount);
        var random = new CountingRandom();
        var mathUtility = new MathUtility(random);

        for (var i = 0; i < 50; i++)
        {
            var cell = registry.CreateCell(id, mathUtility);
            Assert.IsLessThan(variantCount, (int)cell.Variant);
        }

        Assert.AreEqual(50, random.Calls);
    }

    [TestMethod]
    public void TryGetSprite_ReturnsTheManifestCellForTheStoredVariant()
    {
        var registry = new TerrainRegistry();
        var id = registry.Register(Definition("test:grass", spriteName: "Grass"));
        SpriteManifest.TryResolveCell("Grass", 1, out var expected);

        Assert.IsTrue(registry.TryGetSprite(new TerrainCell(id, 1), out var sprite));
        Assert.AreEqual(expected, sprite);
    }
}
