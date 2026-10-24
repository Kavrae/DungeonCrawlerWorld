using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace Tests.UIEngine.Rendering;

/// <summary>How a scope's offset and clip compose with the ones it inherits, and how the glow follows an offset.</summary>
[TestClass]
public sealed class UiRenderStateTests
{
    [TestMethod]
    public void Root_HasNoOffsetAndNoClip()
    {
        Assert.AreEqual(new UiRenderService.RenderState(Vector2.Zero, null), UiRenderService.RenderState.Root);
    }

    [TestMethod]
    public void ClippedTo_FromNoClip_IsTheRectangle()
    {
        var clipped = UiRenderService.RenderState.Root.ClippedTo(new Rectangle(10, 20, 30, 40));

        Assert.AreEqual(new Rectangle(10, 20, 30, 40), clipped.ClipRectangle);
    }

    [TestMethod]
    public void ClippedTo_IntersectsTheInheritedClip_AndKeepsTheOffset()
    {
        var outer = new UiRenderService.RenderState(new Vector2(5, 7), new Rectangle(0, 0, 100, 100));

        var clipped = outer.ClippedTo(new Rectangle(80, 90, 50, 50));

        Assert.AreEqual(new UiRenderService.RenderState(new Vector2(5, 7), new Rectangle(80, 90, 20, 10)), clipped);
    }

    [TestMethod]
    public void ClippedTo_ADisjointRectangle_ClipsEverything()
    {
        var clipped = UiRenderService.RenderState.Root.ClippedTo(new Rectangle(0, 0, 10, 10)).ClippedTo(new Rectangle(20, 20, 10, 10));

        Assert.AreEqual(Rectangle.Empty, clipped.ClipRectangle);
    }

    [TestMethod]
    public void GlowTransformAfterOffset_ReadsAMovedPositionAtItsUnmovedTexel()
    {
        var transform = SpriteEffectGlow.TransformFor(new Point(16, 24), new Vector2(-3, 5), marginTiles: 3);
        var offset = new Vector2(120, -40);
        var drawPosition = new Vector2(37, 61);

        var moved = SpriteEffectGlow.TexelPositionOf(SpriteEffectGlow.TransformAfterOffset(transform, offset), drawPosition + offset);
        var unmoved = SpriteEffectGlow.TexelPositionOf(transform, drawPosition);

        Assert.AreEqual(unmoved.X, moved.X, 0.0001f);
        Assert.AreEqual(unmoved.Y, moved.Y, 0.0001f);
    }
}
