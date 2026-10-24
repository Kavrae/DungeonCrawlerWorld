using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace Presentation.MapRendering;

/// <summary>What MapGameOverlay shows on the visible grid's tiles, as texels for the sprite effect's tile overlay (SpriteEffectTileOverlay): per tile a wash, up to two fills and a ring.</summary>
/// <remarks>
/// Washes over one tile are composited here in the order they are added, so any number reduce to one colour. Fills keep the
/// two with the largest fractions -- the nearest to resolving -- the larger first, so the smaller shows over it; a fill past
/// those is dropped (its backdrop wash still marks the tile). A tile has one ring; setting another replaces it. A tile outside
/// the grid is ignored.
/// </remarks>
public sealed class MapOverlayTexels
{
    private Color[] _texels = [];
    private Point _scroll;
    private bool _hasAnyTexel;

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    /// <summary>Texels per row: SpriteEffectTileOverlay.TexelsPerTile per column.</summary>
    public int Width => Columns * SpriteEffectTileOverlay.TexelsPerTile;

    public int Height => Rows;

    /// <summary>The texels, row by row, Width by Height; longer than that when a larger grid was begun before.</summary>
    public Color[] Texels => _texels;

    /// <summary>Whether anything was added since Begin.</summary>
    public bool IsEmpty => !_hasAnyTexel;

    /// <summary>Starts a frame over the grid whose top-left tile is scroll, with nothing on any tile.</summary>
    public void Begin(Point scroll, int columns, int rows)
    {
        var texelCount = columns * SpriteEffectTileOverlay.TexelsPerTile * rows;
        if (_texels.Length < texelCount)
        {
            _texels = new Color[texelCount];
        }
        else if (_hasAnyTexel)
        {
            Array.Clear(_texels, 0, Width * Height);
        }

        _scroll = scroll;
        Columns = columns;
        Rows = rows;
        _hasAnyTexel = false;
    }

    /// <summary>Lays premultiplied over the tile's wash.</summary>
    public void AddWash(int mapX, int mapY, Color premultiplied)
    {
        if (!TryGetTileStart(mapX, mapY, out var tileStart))
        {
            return;
        }

        ref var wash = ref _texels[tileStart + SpriteEffectTileOverlay.WashTexel];
        wash = new Color(premultiplied.ToVector4() + wash.ToVector4() * (1f - premultiplied.A / 255f));
        _hasAnyTexel = true;
    }

    /// <summary>A fill of premultiplied from the tile's bottom up to fraction of its height.</summary>
    public void AddFill(int mapX, int mapY, Color premultiplied, float fraction)
    {
        if (!TryGetTileStart(mapX, mapY, out var tileStart))
        {
            return;
        }

        _hasAnyTexel = true;
        var fractionByte = SpriteEffectTileOverlay.FractionByte(fraction);
        ref var fractions = ref _texels[tileStart + SpriteEffectTileOverlay.FractionsTexel];
        ref var firstFill = ref _texels[tileStart + SpriteEffectTileOverlay.FirstFillTexel];
        ref var secondFill = ref _texels[tileStart + SpriteEffectTileOverlay.FirstFillTexel + 1];

        if (firstFill == default || fractionByte > fractions.R)
        {
            secondFill = firstFill;
            fractions.G = fractions.R;
            firstFill = premultiplied;
            fractions.R = fractionByte;
            return;
        }

        if (secondFill == default || fractionByte > fractions.G)
        {
            secondFill = premultiplied;
            fractions.G = fractionByte;
        }
    }

    /// <summary>A ring of premultiplied faded inward from the tile's edge over depthPixels, replacing any ring already there.</summary>
    public void SetRing(int mapX, int mapY, Color premultiplied, byte depthPixels)
    {
        if (!TryGetTileStart(mapX, mapY, out var tileStart))
        {
            return;
        }

        _texels[tileStart + SpriteEffectTileOverlay.RingTexel] = premultiplied;
        _texels[tileStart + SpriteEffectTileOverlay.FractionsTexel].B = depthPixels;
        _hasAnyTexel = true;
    }

    /// <summary>The texel texelInTile (one of SpriteEffectTileOverlay's texel constants) of the grid tile at (column, row).</summary>
    public Color TexelAt(int column, int row, int texelInTile) =>
        _texels[(row * Columns + column) * SpriteEffectTileOverlay.TexelsPerTile + texelInTile];

    private bool TryGetTileStart(int mapX, int mapY, out int tileStart)
    {
        var column = mapX - _scroll.X;
        var row = mapY - _scroll.Y;
        if (column < 0 || column >= Columns || row < 0 || row >= Rows)
        {
            tileStart = 0;
            return false;
        }

        tileStart = (row * Columns + column) * SpriteEffectTileOverlay.TexelsPerTile;
        return true;
    }
}
