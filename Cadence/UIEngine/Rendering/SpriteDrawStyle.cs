namespace UIEngine.Rendering;

/// <summary>How the sprite effect shades a draw, beyond its colour.</summary>
public enum SpriteDrawMode : byte
{
    /// <summary>The texel times the draw's colour, as SpriteBatch's default effect draws it.</summary>
    Plain = 0,

    /// <summary>Plain, then grey at the result's luminance: the colours go, the light and dark stay.</summary>
    Desaturate = 1,

    /// <summary>Plain, faded from full a pixel inside the quad's edge to nothing FadeDepthPixels further in: a glow within a rectangle, clear of a 1 px border on its edge.</summary>
    FadeInward = 2,

    /// <summary>Plain, faded from full against a rectangle FadeDepthPixels inside the quad's edge to nothing at that edge, and nothing within the rectangle: a glow around it, drawn on the rectangle grown by the depth.</summary>
    FadeOutward = 3,

    /// <summary>Not the texel: the batch's tile overlay (SpriteEffectTileOverlay) at each pixel -- the tile under it, its wash, fills and ring. Drawn as one quad over the overlay's grid.</summary>
    TileOverlay = 4,
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
/// <param name="FadeDepthPixels">For FadeInward and FadeOutward, how many screen pixels the fade runs over.</param>
public readonly record struct SpriteDrawStyle(SpriteDrawMode Mode, SpriteGlow Glow = SpriteGlow.None, float GroundRowY = 0f, bool OutlineOnly = false, byte FadeDepthPixels = 0)
{
    public static SpriteDrawStyle Plain => default;

    public static SpriteDrawStyle Desaturated => new(SpriteDrawMode.Desaturate);

    public SpriteDrawStyle WithGlowAtPixel() => this with { Glow = SpriteGlow.AtPixel, GroundRowY = 0f };

    public SpriteDrawStyle WithGlowAlongGroundRow(float groundRowY) => this with { Glow = SpriteGlow.AlongGroundRow, GroundRowY = groundRowY };

    public SpriteDrawStyle WithFade(SpriteDrawMode fadeMode, byte depthPixels) => this with { Mode = fadeMode, FadeDepthPixels = depthPixels };

    public SpriteEffectVertexStyle ToVertexStyle() => new((byte)Mode, (byte)Glow, OutlineOnly ? (byte)1 : (byte)0, FadeDepthPixels, GroundRowY);
}
