using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UIEngine.Rendering;

/// <summary>The four corners of a sprite as SpriteBatch.Draw places them, and the runs of quads that share a texture.</summary>
/// <remarks>
/// Corners are written top-left, top-right, bottom-left, bottom-right -- SpriteBatch's order, which its index pattern (and so
/// its winding under the default cull mode) assumes. A corner sits at (offset - origin) * size, rotated about the origin and
/// moved to position; a flip only swaps which corner takes which texture coordinate (the corner's index XOR the flip bits),
/// so the origin is never mirrored -- all as FNA's SpriteBatch does it.
/// </remarks>
internal static class SpriteEffectQuads
{
    public const int VerticesPerQuad = 4;
    public const int IndicesPerQuad = 6;

    private static ReadOnlySpan<float> CornerOffsetX => [0f, 1f, 0f, 1f];
    private static ReadOnlySpan<float> CornerOffsetY => [0f, 0f, 1f, 1f];

    /// <param name="normalizedOrigin">The origin as a fraction of the source's size.</param>
    /// <param name="normalizedSource">The source rectangle as fractions of the texture's size: x, y, width, height.</param>
    public static void Write(Span<SpriteEffectVertex> corners, Vector2 position, Vector2 size, float rotation, Vector2 normalizedOrigin, Vector4 normalizedSource, Color color, SpriteEffects effects, Vector4 style)
    {
        if (rotation == 0f && effects == SpriteEffects.None)
        {
            WriteUpright(corners, position - normalizedOrigin * size, size, normalizedSource, color, style);
            return;
        }

        var sine = rotation == 0f ? 0f : MathF.Sin(rotation);
        var cosine = rotation == 0f ? 1f : MathF.Cos(rotation);
        var flip = (int)effects & 0x03;

        for (var corner = 0; corner < VerticesPerQuad; corner++)
        {
            var cornerX = (CornerOffsetX[corner] - normalizedOrigin.X) * size.X;
            var cornerY = (CornerOffsetY[corner] - normalizedOrigin.Y) * size.Y;
            corners[corner] = new SpriteEffectVertex
            {
                Position = new Vector3(position.X + cornerX * cosine - cornerY * sine, position.Y + cornerX * sine + cornerY * cosine, 0f),
                Color = color,
                TextureCoordinate = new Vector2(
                    normalizedSource.X + CornerOffsetX[corner ^ flip] * normalizedSource.Z,
                    normalizedSource.Y + CornerOffsetY[corner ^ flip] * normalizedSource.W),
                Style = style,
            };
        }
    }

    /// <summary>Write for the common sprite -- unturned, unflipped -- without the per-corner rotation and lookups: most of what the map draws.</summary>
    private static void WriteUpright(Span<SpriteEffectVertex> corners, Vector2 topLeft, Vector2 size, Vector4 normalizedSource, Color color, Vector4 style)
    {
        var right = topLeft.X + size.X;
        var bottom = topLeft.Y + size.Y;
        var sourceRight = normalizedSource.X + normalizedSource.Z;
        var sourceBottom = normalizedSource.Y + normalizedSource.W;

        corners[0] = new SpriteEffectVertex { Position = new Vector3(topLeft.X, topLeft.Y, 0f), Color = color, TextureCoordinate = new Vector2(normalizedSource.X, normalizedSource.Y), Style = style };
        corners[1] = new SpriteEffectVertex { Position = new Vector3(right, topLeft.Y, 0f), Color = color, TextureCoordinate = new Vector2(sourceRight, normalizedSource.Y), Style = style };
        corners[2] = new SpriteEffectVertex { Position = new Vector3(topLeft.X, bottom, 0f), Color = color, TextureCoordinate = new Vector2(normalizedSource.X, sourceBottom), Style = style };
        corners[3] = new SpriteEffectVertex { Position = new Vector3(right, bottom, 0f), Color = color, TextureCoordinate = new Vector2(sourceRight, sourceBottom), Style = style };
    }

    /// <summary>source (or the whole texture) as fractions of a texture of textureSize: x, y, width, height.</summary>
    public static Vector4 NormalizedSource(Rectangle? source, Point textureSize) =>
        source is { } rectangle
            ? new Vector4(rectangle.X / (float)textureSize.X, rectangle.Y / (float)textureSize.Y, rectangle.Width / (float)textureSize.X, rectangle.Height / (float)textureSize.Y)
            : new Vector4(0f, 0f, 1f, 1f);

    /// <summary>How many items from start on are the same object as items[start] -- the quads one draw call can take.</summary>
    public static int RunLength<TItem>(ReadOnlySpan<TItem> items, int start)
        where TItem : class
    {
        var first = items[start];
        var end = start + 1;
        while (end < items.Length && ReferenceEquals(items[end], first))
        {
            end++;
        }

        return end - start;
    }

    /// <summary>The index pattern for quadCount quads: each quad's two triangles over its four corners.</summary>
    public static short[] Indices(int quadCount)
    {
        var indices = new short[quadCount * IndicesPerQuad];
        for (var quad = 0; quad < quadCount; quad++)
        {
            var firstVertex = (short)(quad * VerticesPerQuad);
            var firstIndex = quad * IndicesPerQuad;
            indices[firstIndex] = firstVertex;
            indices[firstIndex + 1] = (short)(firstVertex + 1);
            indices[firstIndex + 2] = (short)(firstVertex + 2);
            indices[firstIndex + 3] = (short)(firstVertex + 1);
            indices[firstIndex + 4] = (short)(firstVertex + 3);
            indices[firstIndex + 5] = (short)(firstVertex + 2);
        }

        return indices;
    }
}
