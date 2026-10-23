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

float4x4 MatrixTransform;
float VertexVersion = 4;
float4 GlowTransform;
float2 GlowTexelSize;

static const float DesaturateMode = 1;
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

struct SpriteVertex
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
    float4 StyleFlags : TEXCOORD1;
    float GroundRowY : TEXCOORD2;
};

struct ShadedVertex
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
    float3 Style : TEXCOORD1;
    float2 GlowTexel : TEXCOORD2;
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
    output.Style = round(input.StyleFlags.xyz * StyleFlagScale);

    float2 glowReadPoint = float2(input.Position.x, Is(output.Style.y, GlowAlongGroundRow) ? input.GroundRowY : input.Position.y);
    output.GlowTexel = glowReadPoint * GlowTransform.xy + GlowTransform.zw;
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

float4 ShadeSprite(ShadedVertex input) : COLOR0
{
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
