using Game.Terrain;
using Microsoft.Xna.Framework;

namespace Tests.Terrain;

[TestClass]
public sealed class TerrainRegistryTests
{
    private static TerrainDefinition Definition(string key, string name = "Sand", string? spriteName = null) =>
        new(key, name, "", "~", Color.White, SpriteName: spriteName);

    private const string WallSpriteName = "wall-stone";

    [TestMethod]
    public void GetLinkGroup_AnExplicitGroup_JoinsDefinitionsDrawnWithDifferentSprites()
    {
        var registry = new TerrainRegistry();
        var stone = registry.Register(Definition("test:stone", spriteName: WallSpriteName) with { LinkGroup = "masonry" });
        var doorFrame = registry.Register(Definition("test:door-frame", spriteName: "door-frame") with { LinkGroup = "masonry" });
        var plainStone = registry.Register(Definition("test:plain-stone", spriteName: WallSpriteName));

        Assert.AreEqual(registry.GetLinkGroup(stone), registry.GetLinkGroup(doorFrame));
        Assert.AreNotEqual(registry.GetLinkGroup(stone), registry.GetLinkGroup(plainStone));
    }

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
}
