using Game.Views;
using Presentation.Rendering;
using Tests.UIEngine;

namespace Tests.Presentation.Rendering;

[TestClass]
public sealed class MapVisualResolutionTests
{
    [TestMethod]
    public void Resolve_AnEntityWhoseSpriteTheManifestLacks_KeepsItsGlyph()
    {
        var visual = TestSprites.Resolver.Resolve(new EntityVisualView("No such sprite", 3, "g", Microsoft.Xna.Framework.Color.Red, IsDead: true));

        Assert.IsNull(visual.Sprite);
        Assert.AreEqual("g", visual.Glyph);
        Assert.IsTrue(visual.IsDead);
    }

    [TestMethod]
    public void Resolve_ATerrainCell_IsItsFloorOptionForTheSeed_OrItsGlyph()
    {
        Assert.IsTrue(TestSprites.Resolver.TryGetTerrainSprites("Grass", out var grass));
        Assert.IsTrue(grass.TryGetFace(7, mask: null, out var expected));

        Assert.AreEqual(expected, TestSprites.Resolver.Resolve(new TerrainVisualView("Grass", 7, ",", default)).Sprite);
        Assert.AreEqual("~", TestSprites.Resolver.Resolve(new TerrainVisualView(null, 7, "~", default)).Glyph);
    }
}
