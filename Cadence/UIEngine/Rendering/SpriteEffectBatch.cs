using FontStashSharp;
using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>SpriteBatch's deferred drawing through the sprite effect: quads in call order, each carrying its draw's style (SpriteEffectVertex).</summary>
/// <remarks>
/// <para>
/// Draws exactly where SpriteBatch would -- the same corners (SpriteEffectQuads), the same premultiplied colour, AlphaBlend, no
/// depth -- so a renderer moved onto it looks the same until it asks for a style. Quads are kept until End, then drawn in the
/// order they were given, one draw call per run of quads sharing a texture, as SpriteBatch's Deferred mode does.
/// </para>
/// <para>
/// Strings reach it through FontStashSharp's IFontStashRenderer, one sprite per glyph -- the route FontStashSharp's own
/// SpriteBatch drawing takes, measured faster than its quad-level IFontStashRenderer2 -- so text stays in the same order as
/// everything around it. Positions are mapped by an orthographic projection of the viewport at End, after the Begin's
/// transform -- SpriteBatch's own mapping.
/// </para>
/// <para>
/// Outlined text is two draws per glyph, not ContrastTextRenderer's nine, in the nine's order: the whole string's outline
/// layer (SpriteDrawStyle.OutlineOnly), then its fill, so no glyph's outline covers a neighbour's fill. An outline layer's
/// source and quad grow by a texel on every side -- FontStashSharp's atlas leaves a clear gap of more than that between
/// glyphs, so nothing of a neighbour is read -- and the sprite effect draws into it what the eight black copies would.
/// </para>
/// </remarks>
public sealed class SpriteEffectBatch : ISpriteDrawTarget, IFontStashRenderer
{
    private const int QuadsPerDrawBuffer = 2048;

    private readonly EffectParameter _matrixTransform;
    private readonly EffectParameter _glowTexture;
    private readonly EffectParameter _glowTransform;
    private readonly EffectParameter _glowTexelSize;
    private readonly EffectPass _pass;

    /// <summary>One transparent texel: what lit draws read when the batch has no glow, so they come out unlit.</summary>
    private readonly Texture2D _noGlowTexture;
    private readonly DynamicVertexBuffer _vertexBuffer;
    private readonly IndexBuffer _indexBuffer;

    private SpriteEffectVertex[] _vertices = new SpriteEffectVertex[QuadsPerDrawBuffer * SpriteEffectQuads.VerticesPerQuad];
    private Texture2D[] _textures = new Texture2D[QuadsPerDrawBuffer];
    private int _quadCount;

    private bool _isDrawing;
    private SamplerState _samplerState = SamplerState.PointClamp;
    private RasterizerState _rasterizerState = RasterizerState.CullCounterClockwise;
    private Matrix _transformMatrix = Matrix.Identity;

    /// <summary>The style IFontStashRenderer.Draw gives the glyphs of the string DrawString or DrawOutlinedString is drawing.</summary>
    private SpriteDrawStyle _textStyle;

    public SpriteEffectBatch(GraphicsDevice graphicsDevice, Effect spriteEffect)
    {
        GraphicsDevice = graphicsDevice;
        _matrixTransform = spriteEffect.Parameters["MatrixTransform"];
        _glowTexture = spriteEffect.Parameters["GlowTexture"];
        _glowTransform = spriteEffect.Parameters["GlowTransform"];
        _glowTexelSize = spriteEffect.Parameters["GlowTexelSize"];
        _pass = spriteEffect.CurrentTechnique.Passes[0];
        _noGlowTexture = new Texture2D(graphicsDevice, 1, 1);
        _noGlowTexture.SetData([Color.Transparent]);
        _vertexBuffer = new DynamicVertexBuffer(graphicsDevice, SpriteEffectVertex.Declaration, QuadsPerDrawBuffer * SpriteEffectQuads.VerticesPerQuad, BufferUsage.WriteOnly);
        _indexBuffer = new IndexBuffer(graphicsDevice, IndexElementSize.SixteenBits, QuadsPerDrawBuffer * SpriteEffectQuads.IndicesPerQuad, BufferUsage.WriteOnly);
        _indexBuffer.SetData(SpriteEffectQuads.Indices(QuadsPerDrawBuffer));
    }

    public GraphicsDevice GraphicsDevice { get; }

    /// <summary>The light this pass's lit draws read; None (unlit) from each Begin until set.</summary>
    public SpriteEffectGlow Glow { get; set; }

    /// <param name="rasterizerState">Null for SpriteBatch's default (CullCounterClockwise); a scissor-clipping state to clip.</param>
    /// <param name="transformMatrix">Applied to every position before the viewport's projection, as SpriteBatch.Begin's.</param>
    /// <exception cref="InvalidOperationException">A Begin is already open.</exception>
    public void Begin(SamplerState samplerState, RasterizerState? rasterizerState, Matrix transformMatrix)
    {
        if (_isDrawing)
        {
            throw new InvalidOperationException("SpriteEffectBatch.Begin was called again before End.");
        }

        _isDrawing = true;
        _samplerState = samplerState;
        _rasterizerState = rasterizerState ?? RasterizerState.CullCounterClockwise;
        _transformMatrix = transformMatrix;
        Glow = SpriteEffectGlow.None;
    }

    /// <summary>Draws every quad since Begin, in order.</summary>
    /// <exception cref="InvalidOperationException">No Begin is open.</exception>
    public void End()
    {
        if (!_isDrawing)
        {
            throw new InvalidOperationException("SpriteEffectBatch.End was called without Begin.");
        }

        _isDrawing = false;
        Flush();
    }

    public void Draw(Texture2D texture, Rectangle destination, Rectangle? source, Color color, SpriteDrawStyle style) =>
        AddQuad(texture, new Vector2(destination.X, destination.Y), new Vector2(destination.Width, destination.Height), 0f, Vector2.Zero, SpriteEffectQuads.NormalizedSource(source, new Point(texture.Width, texture.Height)), color, SpriteEffects.None, style);

    public void Draw(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, SpriteDrawStyle style)
    {
        var sourceSize = source is { } rectangle ? new Vector2(rectangle.Width, rectangle.Height) : new Vector2(texture.Width, texture.Height);
        var normalizedOrigin = new Vector2(sourceSize.X == 0f ? 0f : origin.X / sourceSize.X, sourceSize.Y == 0f ? 0f : origin.Y / sourceSize.Y);
        AddQuad(texture, position, sourceSize * scale, rotation, normalizedOrigin, SpriteEffectQuads.NormalizedSource(source, new Point(texture.Width, texture.Height)), color, effects, style);
    }

    public void DrawString(SpriteFontBase font, string text, Vector2 position, Color color, SpriteDrawStyle style)
    {
        _textStyle = style;
        font.DrawText(this, text, position, color);
        _textStyle = default;
    }

    public void DrawOutlinedString(SpriteFontBase font, string text, Vector2 position, Color fillColor, float alphaMultiplier, SpriteDrawStyle style)
    {
        _textStyle = style with { OutlineOnly = true };
        font.DrawText(this, text, position, ContrastTextRenderer.OutlineColor * alphaMultiplier);
        _textStyle = style;
        font.DrawText(this, text, position, fillColor * alphaMultiplier);
        _textStyle = default;
    }

    /// <summary>One glyph; an outline layer's source and quad grown by a texel on every side, its origin moved with them so the glyph itself stays put.</summary>
    void IFontStashRenderer.Draw(Texture2D texture, Vector2 position, Rectangle? source, Color color, float rotation, Vector2 scale, float depth)
    {
        if (_textStyle.OutlineOnly && source is { } glyph)
        {
            Draw(texture, position, new Rectangle(glyph.X - 1, glyph.Y - 1, glyph.Width + 2, glyph.Height + 2), color, rotation, Vector2.One, scale, SpriteEffects.None, _textStyle);
            return;
        }

        Draw(texture, position, source, color, rotation, Vector2.Zero, scale, SpriteEffects.None, _textStyle);
    }

    private void AddQuad(Texture2D texture, Vector2 position, Vector2 size, float rotation, Vector2 normalizedOrigin, Vector4 normalizedSource, Color color, SpriteEffects effects, SpriteDrawStyle style) =>
        SpriteEffectQuads.Write(ReserveQuad(texture), position, size, rotation, normalizedOrigin, normalizedSource, color, effects, style.ToVertexStyle());

    /// <summary>The next quad's four corners, after recording its texture.</summary>
    /// <exception cref="InvalidOperationException">No Begin is open.</exception>
    private Span<SpriteEffectVertex> ReserveQuad(Texture2D texture)
    {
        if (!_isDrawing)
        {
            throw new InvalidOperationException("SpriteEffectBatch draws only between Begin and End.");
        }

        if (_quadCount == _textures.Length)
        {
            Array.Resize(ref _textures, _textures.Length * 2);
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        _textures[_quadCount] = texture;
        var corners = _vertices.AsSpan(_quadCount * SpriteEffectQuads.VerticesPerQuad, SpriteEffectQuads.VerticesPerQuad);
        _quadCount++;
        return corners;
    }

    private void Flush()
    {
        if (_quadCount == 0)
        {
            return;
        }

        var graphicsDevice = GraphicsDevice;
        var viewport = graphicsDevice.Viewport;
        _matrixTransform.SetValue(_transformMatrix * Matrix.CreateOrthographicOffCenter(0f, viewport.Width, viewport.Height, 0f, 0f, 1f));
        var glow = Glow;
        _glowTexture.SetValue(glow.Texture ?? _noGlowTexture);
        _glowTransform.SetValue(glow.Texture is null ? Vector4.Zero : glow.Transform);
        _glowTexelSize.SetValue(glow.Texture is null ? Vector2.One : Vector2.One / glow.TextureSize);

        graphicsDevice.BlendState = BlendState.AlphaBlend;
        graphicsDevice.DepthStencilState = DepthStencilState.None;
        graphicsDevice.RasterizerState = _rasterizerState;
        _pass.Apply();
        graphicsDevice.SamplerStates[0] = _samplerState;
        graphicsDevice.SetVertexBuffer(_vertexBuffer);
        graphicsDevice.Indices = _indexBuffer;

        for (var bufferStart = 0; bufferStart < _quadCount; bufferStart += QuadsPerDrawBuffer)
        {
            var bufferQuads = Math.Min(QuadsPerDrawBuffer, _quadCount - bufferStart);
            _vertexBuffer.SetData(_vertices, bufferStart * SpriteEffectQuads.VerticesPerQuad, bufferQuads * SpriteEffectQuads.VerticesPerQuad, SetDataOptions.Discard);

            var bufferTextures = new ReadOnlySpan<Texture2D>(_textures, bufferStart, bufferQuads);
            for (var runStart = 0; runStart < bufferQuads;)
            {
                var runLength = SpriteEffectQuads.RunLength(bufferTextures, runStart);
                graphicsDevice.Textures[0] = bufferTextures[runStart];
                graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, runStart * SpriteEffectQuads.VerticesPerQuad, 0, runLength * SpriteEffectQuads.VerticesPerQuad, 0, runLength * 2);
                runStart += runLength;
            }
        }

        Array.Clear(_textures, 0, _quadCount);
        _quadCount = 0;
    }
}
