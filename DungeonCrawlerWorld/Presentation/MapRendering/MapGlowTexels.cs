using Engine.Math;
using Game.Views;
using Microsoft.Xna.Framework;
using SpriteFormat;

namespace Presentation.MapRendering;

/// <summary>The auras' glow around the visible grid as texels for the sprite effect to read (SpriteEffectGlow): one per tile, premultiplied, transparent where no aura reaches or the tile is off the map.</summary>
/// <remarks>
/// Covers the grid plus MarginTiles on every side: the overscan the row walk draws sprites from (a sprite anchored just
/// outside the grid reads the tile it stands on), and one more so filtering at the overscan's edge reads a real neighbour.
/// A tile's texel is its blended glow colour (AuraGlowBlend) times its opacity -- its power's share of FullGlowPower times the
/// auras' blended glow opacity multiplier (AuraDefinition.GlowOpacityMultiplier), at most fully opaque.
/// </remarks>
public sealed class MapGlowTexels(IMapViewQuery mapView, AuraGlowBlend glowBlend)
{
    public static readonly int MarginTiles = Math.Max(SpriteReach.MaximumColumnsBeside, SpriteReach.MaximumRowsAbove) + 1;

    /// <summary>The summed aura power at a tile that glows at full strength; anything more glows no brighter.</summary>
    public const int FullGlowPower = 8;

    private Color[] _texels = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The last Build's texels, row by row, Width by Height; longer than that when a larger grid was built before.</summary>
    public Color[] Texels => _texels;

    /// <summary>Fills the texels for the grid key describes.</summary>
    public void Build(MapImageKey key)
    {
        Width = key.TileColumns + MarginTiles * 2;
        Height = key.TileRows + MarginTiles * 2;
        if (_texels.Length < Width * Height)
        {
            _texels = new Color[Width * Height];
        }

        glowBlend.BeginPass();
        for (var row = 0; row < Height; row++)
        {
            for (var column = 0; column < Width; column++)
            {
                var mapX = key.Scroll.X + column - MarginTiles;
                var mapY = key.Scroll.Y + row - MarginTiles;
                _texels[row * Width + column] = GlowAt(mapX, mapY, key.MapLayer);
            }
        }
    }

    /// <summary>The texel of the grid tile at (column, row), which may lie in the margin (negative, or past the grid).</summary>
    public Color TexelAt(int column, int row) => _texels[(row + MarginTiles) * Width + column + MarginTiles];

    private Color GlowAt(int mapX, int mapY, int mapLayer)
    {
        if (!mapView.IsOnMap(new Vector3Int(mapX, mapY, 0)) || !glowBlend.TryGetGlow(mapX, mapY, mapLayer, out var glowColor, out var totalPower, out var glowOpacityMultiplier))
        {
            return Color.Transparent;
        }

        var opacity = Math.Min(Math.Min(totalPower, FullGlowPower) / (float)FullGlowPower * glowOpacityMultiplier, 1f);
        return glowColor * opacity;
    }
}
