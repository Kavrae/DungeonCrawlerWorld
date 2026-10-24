// The map's sprite effect, drawn through SpriteEffectBatch: SpriteEffectVertex in (position, colour, texture coordinate,
// style flags, ground row), the sprite's texel times its colour out -- what SpriteBatch's default effect draws -- or a glyph's
// outline when the style asks (StyleFlags.z), shaded by the style's mode (StyleFlags.x, a SpriteDrawMode value), then lit by the
// glow texture as the style asks (StyleFlags.y, a SpriteGlow value; GroundRowY the ground row's y). The flags arrive as bytes
// normalized to [0, 1] and are scaled back to whole values once per vertex; every comparison of one allows half a step either
// side, so interpolation can never tip it. VertexVersion is checked against SpriteEffectVertex.Version when a content file
// overrides this effect (SpriteEffectService).
//
// Outlined text is two draws per glyph instead of ContrastTextRenderer's nine, in the nine's order: every glyph's outline
// layer, then every glyph's fill (so no glyph's outline covers its neighbour's fill). An outline layer is drawn from a quad grown
// by a texel on every side and is black covering what the 8 neighbouring texels cover together at the draw's alpha -- exactly
// what eight overlapping black copies a pixel apart compound to, faded or not. Neighbours are one texel apart, read from the
// texture coordinate's change per screen pixel, so the outline is a screen pixel wide at whatever size the glyph is drawn.
// The neighbour loops are unrolled: eight independent reads, the centre left out at compile time.
//
// The glow texture holds one texel per tile, premultiplied (SpriteEffectGlow). GlowTransform maps a draw position to texel
// coordinates (xy scale, zw offset) so a tile's centre lands on its texel's centre, and the texture is read with linear
// filtering, so light shades smoothly across tiles. The mapping is affine, so the vertex shader applies it to the read point
// (the pixel itself, or its column on the ground row) and the pixel shader receives texel coordinates; GlowTexelSize is one
// texel in texture coordinates. Where the tile under the read point has no glow, nothing is lit, so the light never spreads
// past the last tile it reaches.
//
// A fade (FadeInwardMode, FadeOutwardMode) is a glow drawn as one quad of the unit rectangle, so the texture coordinate runs
// 0-1 across it: a pixel's distance to the quad's edge in screen pixels is that coordinate's distance to 0 or 1 over its change
// per pixel. The draw's colour fades linearly over StyleFlags.w pixels -- full at the start, nothing at the depth: inward from a
// pixel inside the edge (a window's 1 px border sits on it) to a clear area the depth further in, outward from the rectangle
// the quad was grown around, which stays clear. Distance to the clear area or the rectangle is Euclidean, so a fade's corners
// are rounded: outward, its outer edge; inward, the clear area's.
//
// Every draw carries a clip rectangle in draw space (ClipRectangle: left, top inclusive; right, bottom exclusive), and the
// pixel shader discards a pixel outside it -- how a scrolled element clips its content without ending the batch. The vertex
// shader turns it into four distances from the vertex to its edges, which interpolate exactly across the quad; the pixel
// shader discards on the nearest (a temporary: MojoShader refuses a discard straight from an input register).
//
// The tile overlay (TileOverlayMode, SpriteEffectTileOverlay) is drawn as one quad over a grid of tiles, and each pixel shows
// its tile's layers instead of a texel: TileOverlayTransform maps draw space to tile coordinates (tile (0,0) spans [0, 1)),
// and the tile's TileOverlayTexelsPerTile texels, read point-sampled, are its wash, its fills (from the tile's bottom up to
// their fraction, the top edge anti-aliased over a pixel), its ring (an inward fade, as FadeInwardMode's) and the fractions
// -- premultiplied, laid over each other in that order.

float4x4 MatrixTransform;
float VertexVersion = 7;
float4 GlowTransform;
float2 GlowTexelSize;
float4 TileOverlayTransform;
float2 TileOverlayTexelSize;

static const float DesaturateMode = 1;
static const float FadeInwardMode = 2;
static const float FadeOutwardMode = 3;
static const float TileOverlayMode = 4;
static const float TileOverlayTexelsPerTile = 5;
static const float TileOverlayWashTexel = 0;
static const float TileOverlayFirstFillTexel = 1;
static const float TileOverlayRingTexel = 3;
static const float TileOverlayFractionsTexel = 4;
static const float NoGlow = 0;
static const float GlowAtPixel = 1;
static const float GlowAlongGroundRow = 2;
static const float OutlineOnly = 1;
static const float StyleFlagScale = 255;
static const float3 LuminanceWeights = float3(0.299, 0.587, 0.114);

sampler SpriteSampler : register(s0);

texture GlowTexture;
sampler GlowSampler : register(s1) = sampler_state
{
    Texture = <GlowTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = None;
    AddressU = Clamp;
    AddressV = Clamp;
};

texture TileOverlayTexture;
sampler TileOverlaySampler : register(s2) = sampler_state
{
    Texture = <TileOverlayTexture>;
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = None;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct SpriteVertex
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
    float4 StyleFlags : TEXCOORD1;
    float GroundRowY : TEXCOORD2;
    float4 ClipRectangle : TEXCOORD3;
};

struct ShadedVertex
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
    float4 Style : TEXCOORD1;
    float2 GlowTexel : TEXCOORD2;
    float4 ClipDistances : TEXCOORD3;
    float2 OverlayTile : TEXCOORD4;
};

bool Is(float flag, float value)
{
    return abs(flag - value) < 0.5;
}

ShadedVertex TransformSprite(SpriteVertex input)
{
    ShadedVertex output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    output.Style = round(input.StyleFlags * StyleFlagScale);

    float2 glowReadPoint = float2(input.Position.x, Is(output.Style.y, GlowAlongGroundRow) ? input.GroundRowY : input.Position.y);
    output.GlowTexel = glowReadPoint * GlowTransform.xy + GlowTransform.zw;
    output.ClipDistances = float4(input.Position.xy - input.ClipRectangle.xy, input.ClipRectangle.zw - input.Position.xy);
    output.OverlayTile = input.Position.xy * TileOverlayTransform.xy + TileOverlayTransform.zw;
    return output;
}

float4 GlowAt(float2 glowTexel)
{
    float4 ownTile = tex2Dlod(GlowSampler, float4((floor(glowTexel) + 0.5) * GlowTexelSize, 0, 0));
    if (ownTile.a <= 0)
    {
        return float4(0, 0, 0, 0);
    }

    return tex2Dlod(GlowSampler, float4(glowTexel * GlowTexelSize, 0, 0));
}

float4 Lit(float4 shaded, float4 glow)
{
    shaded.rgb = shaded.rgb * (1 - glow.a) + glow.rgb * shaded.a;
    return shaded;
}

float4 OutlineOf(float2 texCoord, float2 texelAcross, float2 texelDown, float alpha)
{
    float clear = 1;
    [unroll] for (int down = -1; down <= 1; down++)
    {
        [unroll] for (int across = -1; across <= 1; across++)
        {
            if (across != 0 || down != 0)
            {
                clear *= 1 - alpha * tex2Dlod(SpriteSampler, float4(texCoord + across * texelAcross + down * texelDown, 0, 0)).a;
            }
        }
    }

    return float4(0, 0, 0, 1 - clear);
}

float InwardFadeAt(float2 pixelsFromEdges, float depthPixels)
{
    float pixelsOutsideClearArea = length(max(depthPixels + 1 - pixelsFromEdges, 0));
    return min(pixelsFromEdges.x, pixelsFromEdges.y) > 1 && pixelsOutsideClearArea > 0 ? saturate((pixelsOutsideClearArea + 0.5) / depthPixels) : 0;
}

float FadeOf(float2 texCoord, float2 texelAcross, float2 texelDown, float mode, float depthPixels)
{
    float2 texCoordPerPixel = float2(length(float2(texelAcross.x, texelDown.x)), length(float2(texelAcross.y, texelDown.y)));
    float2 pixelsFromEdges = min(texCoord, 1 - texCoord) / max(texCoordPerPixel, 0.000001);
    if (Is(mode, FadeInwardMode))
    {
        return InwardFadeAt(pixelsFromEdges, depthPixels);
    }

    float pixelsOutsideRectangle = length(max(depthPixels - pixelsFromEdges, 0));
    return pixelsOutsideRectangle > 0 ? saturate(1 - (pixelsOutsideRectangle - 0.5) / depthPixels) : 0;
}

float4 TileOverlayTexelAt(float2 tile, float texelIndex)
{
    float2 texel = float2(tile.x * TileOverlayTexelsPerTile + texelIndex + 0.5, tile.y + 0.5);
    return tex2Dlod(TileOverlaySampler, float4(texel * TileOverlayTexelSize, 0, 0));
}

float4 Over(float4 top, float4 under)
{
    return top + under * (1 - top.a);
}

float4 TileOverlayAt(float2 overlayTile)
{
    float2 tile = floor(overlayTile);
    float2 withinTile = overlayTile - tile;
    float2 tilePixels = 1 / TileOverlayTransform.xy;
    float4 fractions = TileOverlayTexelAt(tile, TileOverlayFractionsTexel);
    float pixelsAboveBottom = (1 - withinTile.y) * tilePixels.y;

    float4 shaded = TileOverlayTexelAt(tile, TileOverlayWashTexel);
    shaded = Over(TileOverlayTexelAt(tile, TileOverlayFirstFillTexel) * saturate(fractions.x * tilePixels.y - pixelsAboveBottom + 0.5), shaded);
    shaded = Over(TileOverlayTexelAt(tile, TileOverlayFirstFillTexel + 1) * saturate(fractions.y * tilePixels.y - pixelsAboveBottom + 0.5), shaded);

    float ringDepthPixels = round(fractions.z * StyleFlagScale);
    if (ringDepthPixels > 0)
    {
        shaded = Over(TileOverlayTexelAt(tile, TileOverlayRingTexel) * InwardFadeAt(min(withinTile, 1 - withinTile) * tilePixels, ringDepthPixels), shaded);
    }

    return shaded;
}

float4 ShadeSprite(ShadedVertex input) : COLOR0
{
    clip(min(min(input.ClipDistances.x, input.ClipDistances.y), min(input.ClipDistances.z, input.ClipDistances.w)));

    float2 texelAcross = ddx(input.TexCoord);
    float2 texelDown = ddy(input.TexCoord);
    float4 shaded = tex2D(SpriteSampler, input.TexCoord) * input.Color;

    if (Is(input.Style.z, OutlineOnly))
    {
        shaded = OutlineOf(input.TexCoord, texelAcross, texelDown, input.Color.a);
    }

    if (Is(input.Style.x, DesaturateMode))
    {
        shaded.rgb = dot(shaded.rgb, LuminanceWeights);
    }
    else if (Is(input.Style.x, TileOverlayMode))
    {
        shaded = TileOverlayAt(input.OverlayTile);
    }
    else if (input.Style.x > DesaturateMode + 0.5)
    {
        shaded *= FadeOf(input.TexCoord, texelAcross, texelDown, input.Style.x, input.Style.w);
    }

    if (!Is(input.Style.y, NoGlow))
    {
        shaded = Lit(shaded, GlowAt(input.GlowTexel));
    }

    return shaded;
}

technique Sprite
{
    pass Draw
    {
        VertexShader = compile vs_3_0 TransformSprite();
        PixelShader = compile ps_3_0 ShadeSprite();
    }
}
