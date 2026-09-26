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
        SpriteManifest.TryGetCell("Grass", 1, out var expected);

        Assert.IsTrue(registry.TryGetSprite(new TerrainCell(id, 1), out var sprite));
        Assert.AreEqual(expected, sprite);
    }
}
