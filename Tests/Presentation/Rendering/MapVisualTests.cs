using Microsoft.Xna.Framework;
using Presentation.Rendering;
using UIEngine.Rendering;

namespace Tests.Presentation.Rendering;

[TestClass]
public sealed class MapVisualTests
{
    [TestMethod]
    public void Corpse_IsDesaturated()
    {
        var visual = new MapVisual(null, "g", Color.Red, IsDead: true);

        Assert.AreEqual(SpriteDrawStyle.Desaturated, visual.DrawStyle);
    }

    [TestMethod]
    public void LivingEntity_IsPlain()
    {
        var visual = new MapVisual(null, "g", Color.Red);

        Assert.AreEqual(SpriteDrawStyle.Plain, visual.DrawStyle);
    }
}
