using SpriteFormat;

namespace Presentation.Rendering;

/// <summary>One manifest entry's sprites as terrain draws them, resolved once: its face (the whole picture for a floor) and, for a wall, its top.</summary>
/// <remarks>
/// A cell's seed (Game.World.CellSeeds), reduced to a variant by VariantCount, picks the variant set of each part (SpriteLinks.SetIndexFor,
/// by chance when the part gives chances): an option for a plain entry, a set of pieces for a linked one, wrapping by each part's own set
/// count, so a wall with two face sets and one top set uses both faces and the one top. Face and top read the same seed, so when both
/// give chances, matching chances pick matching sets (a mossy face under a mossy top). Within a linked set the mask picks the piece, and
/// an empty slot falls back as SpriteLinks.CellIndexFor says (a face to its standalone piece, a top to its fully joined one).
/// </remarks>
public sealed class TerrainSpriteSet
{
    private readonly SpriteManifestPart _face;
    private readonly SpriteView?[] _faceSprites;
    private readonly SpriteManifestPart? _top;
    private readonly SpriteView?[] _topSprites;

    public TerrainSpriteSet(SpriteManifestEntry entry)
    {
        _face = entry.Face;
        _faceSprites = ResolvePart(_face);
        _top = entry.Top;
        _topSprites = _top is null ? [] : ResolvePart(_top);

        var faceSets = _face.Cells.Count / SpriteLinks.CellsPerSet(_face.Linking);
        var topSets = _top is null ? 0 : _top.Cells.Count / SpriteLinks.CellsPerSet(_top.Linking);
        VariantCount = _face.VariantWeights is not null || _top?.VariantWeights is not null
            ? SpriteLinks.PercentileRange
            : Math.Max(faceSets, topSets);
        TopRaisedRows = _face.HeightRows();
    }

    /// <summary>How the face joins its neighbours: Horizontal for a wall's face, None for a floor or a plain face.</summary>
    public SpriteLinking FaceLinking => _face.Linking;

    /// <summary>How the top joins its neighbours; None when there is no top.</summary>
    public SpriteLinking TopLinking => _top?.Linking ?? SpriteLinking.None;

    /// <summary>How many variants a cell's seed is reduced to: a percentile (100) when either part gives its variant sets chances, so each part maps the one variant through its own chances; otherwise the larger of the face's and the top's set counts.</summary>
    public int VariantCount { get; }

    /// <summary>How many rows above the wall's tile its top sits: the face's height (SpriteManifestPart.HeightRows), 1 for a face stretched to its tile.</summary>
    public int TopRaisedRows { get; }

    public bool HasTop => _top is not null;

    /// <summary>The face piece a cell of seed uses for mask -- null for the piece the set falls back to; mask is ignored for a plain (options) face.</summary>
    public bool TryGetFace(uint seed, int? mask, out SpriteView sprite) => TryGet(_face, _faceSprites, VariantFor(seed), mask, out sprite);

    /// <summary>The top piece a cell of seed uses for mask -- null for the piece the set falls back to; false for an entry with no top.</summary>
    public bool TryGetTop(uint seed, int? mask, out SpriteView sprite)
    {
        if (_top is not null)
        {
            return TryGet(_top, _topSprites, VariantFor(seed), mask, out sprite);
        }

        sprite = default;
        return false;
    }

    /// <summary>The variant seed picks, the same for face and top.</summary>
    private int VariantFor(uint seed) => VariantCount == 0 ? 0 : (int)(seed % (uint)VariantCount);

    private static SpriteView?[] ResolvePart(SpriteManifestPart part)
    {
        var sprites = new SpriteView?[part.Cells.Count];
        for (var index = 0; index < sprites.Length; index++)
        {
            if (part.Cells[index] is { } cell)
            {
                sprites[index] = SpriteManifestResolver.ToSpriteView(cell, part.PlacementFor(cell));
            }
        }

        return sprites;
    }

    private static bool TryGet(SpriteManifestPart part, SpriteView?[] sprites, int variant, int? mask, out SpriteView sprite)
    {
        var index = part.Linking == SpriteLinking.None
            ? sprites.Length == 0 ? -1 : variant % sprites.Length
            : SpriteLinks.CellIndexFor(part, variant, mask ?? SpriteLinks.FallbackMask(part.Linking));

        if (index >= 0 && sprites[index] is { } found)
        {
            sprite = found;
            return true;
        }

        sprite = default;
        return false;
    }
}
