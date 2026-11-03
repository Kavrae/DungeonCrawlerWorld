using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UIEngine.Rendering;

namespace Presentation.MapRendering;

/// <summary>Draws a ground mark as a flat ellipse in the lower part of its footprint: one white ring texture, built on first draw, stretched and tinted by the mark.</summary>
/// <remarks>The ring is opaque at its rim and fades inward to a faint fill, so the ground under it stays legible. The texture is sampled point-clamped like every other draw, so its rim fades over a few texels rather than one.</remarks>
public sealed class MapGroundRing : IDisposable
{
    private const int TextureSize = 64;

    /// <summary>The ellipse's width as a fraction of the footprint's; the same margin is left below it.</summary>
    private const float WidthFraction = 0.9f;

    /// <summary>The ellipse's height as a fraction of its width: a circle seen from above at an angle.</summary>
    private const float HeightToWidth = 0.5f;

    /// <summary>How far in from the rim, as a fraction of the radius, the ring stays fully opaque.</summary>
    private const float RimFraction = 0.12f;

    /// <summary>How far past the rim, as a fraction of the radius, the ring fades down to the fill.</summary>
    private const float FadeFraction = 0.3f;

    /// <summary>The faint fill inside the fade, so the ellipse reads as a disc on the ground rather than an outline.</summary>
    private const float FillAlpha = 0.15f;

    /// <summary>How many texels the outer edge fades over.</summary>
    private const float EdgeSoftnessTexels = 2f;

    private const float RimAlpha = 0.9f;

    private Texture2D? _texture;

    public void Draw(SpriteEffectBatch batch, Color color, Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var texture = _texture ??= CreateTexture(batch.GraphicsDevice);
        var (topLeft, size) = Ellipse(footprintTopLeft, footprintSize);
        batch.Draw(texture, topLeft, null, color, 0f, Vector2.Zero, size / TextureSize, SpriteEffects.None);
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
    }

    /// <summary>Where the ellipse under a footprint lands: centred across it, as wide as WidthFraction of it, its bottom as far above the footprint's bottom as its sides are inside the footprint's.</summary>
    public static (Vector2 TopLeft, Vector2 Size) Ellipse(Vector2 footprintTopLeft, Vector2 footprintSize)
    {
        var width = footprintSize.X * WidthFraction;
        var size = new Vector2(width, MathF.Min(width * HeightToWidth, footprintSize.Y));
        var margin = (footprintSize.X - width) / 2f;
        return (new Vector2(footprintTopLeft.X + margin, footprintTopLeft.Y + footprintSize.Y - margin - size.Y), size);
    }

    /// <summary>The ring's alpha at distance from the centre, as a fraction of the radius.</summary>
    public static float AlphaAt(float distanceFraction, float radiusTexels)
    {
        if (distanceFraction >= 1f)
        {
            return 0f;
        }

        var fromRim = 1f - distanceFraction;
        var edge = MathF.Min(1f, fromRim * radiusTexels / EdgeSoftnessTexels);
        var rimStrength = fromRim <= RimFraction ? 1f : MathF.Max(0f, 1f - (fromRim - RimFraction) / FadeFraction);
        return edge * MathHelper.Lerp(FillAlpha, RimAlpha, rimStrength);
    }

    private static Texture2D CreateTexture(GraphicsDevice graphicsDevice)
    {
        var radius = TextureSize / 2f;
        var texels = new Color[TextureSize * TextureSize];
        for (var y = 0; y < TextureSize; y++)
        {
            for (var x = 0; x < TextureSize; x++)
            {
                var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                texels[y * TextureSize + x] = Color.White * AlphaAt(distance / radius, radius);
            }
        }

        var texture = new Texture2D(graphicsDevice, TextureSize, TextureSize);
        texture.SetData(texels);
        return texture;
    }
}
