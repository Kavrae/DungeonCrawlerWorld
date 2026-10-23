using Microsoft.Xna.Framework;

namespace UIEngine.Rendering;

/// <summary>How the sprite effect shades a draw, beyond its colour.</summary>
public enum SpriteDrawMode : byte
{
    /// <summary>The texel times the draw's colour, as SpriteBatch's default effect draws it.</summary>
    Plain = 0,

    /// <summary>Plain, then grey at the result's luminance: the colours go, the light and dark stay.</summary>
    Desaturate = 1,
}

/// <summary>Where a draw reads the batch's glow texture (SpriteEffectGlow), if at all.</summary>
public enum SpriteGlow : byte
{
    /// <summary>Unlit.</summary>
    None = 0,

    /// <summary>Lit by the glow under each pixel: a floor's full gradient.</summary>
    AtPixel = 1,

    /// <summary>Lit by the glow under each pixel's column on the ground row: something standing is lit side to side like the floor under it, and nothing it reaches above that row changes it.</summary>
    AlongGroundRow = 2,
}

/// <summary>What a draw asks of the sprite effect: carried in each SpriteEffectVertex, ignored by a SpriteBatch.</summary>
/// <param name="GroundRowY">For AlongGroundRow, the y in draw space the glow is read along -- the centre of the footprint's lowest row.</param>
/// <param name="OutlineOnly">A glyph drawn as its one-pixel black outline -- the layer SpriteEffectBatch.DrawOutlinedString draws under the fill.</param>
public readonly record struct SpriteDrawStyle(SpriteDrawMode Mode, SpriteGlow Glow = SpriteGlow.None, float GroundRowY = 0f, bool OutlineOnly = false)
{
    public static SpriteDrawStyle Plain => default;

    public static SpriteDrawStyle Desaturated => new(SpriteDrawMode.Desaturate);

    public SpriteDrawStyle WithGlowAtPixel() => this with { Glow = SpriteGlow.AtPixel, GroundRowY = 0f };

    public SpriteDrawStyle WithGlowAlongGroundRow(float groundRowY) => this with { Glow = SpriteGlow.AlongGroundRow, GroundRowY = groundRowY };

    public Vector4 ToVertexStyle() => new((float)Mode, (float)Glow, GroundRowY, OutlineOnly ? 1f : 0f);
}
