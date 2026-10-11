using Game.Sprites;

namespace Game.Terrain;

/// <summary>One terrain definition's sprites, resolved once from its manifest entry: its face (the whole picture for a floor) and, for a wall, its top.</summary>
/// <remarks>
/// A cell's Variant picks the variant set of each part (SpriteLinks.SetIndexFor, by chance when the part gives chances): an option for a plain entry, a set of pieces for a linked one, wrapping by each
/// part's own set count, so a wall with two face sets and one top set uses both faces and the one top. Face and top read the same
/// roll, so when both give chances, matching chances pick matching sets (a mossy face under a mossy top). Within a linked set the mask
/// picks the piece, and an empty slot falls back as SpriteLinks.CellIndexFor says (a face to its standalone piece, a top to its fully
/// joined one).
/// </remarks>
public sealed class TerrainSpriteSet
{
    private readonly SpriteManifestPart _face;
    private readonly ManifestSprite?[] _faceSprites;
    private readonly SpriteManifestPart? _top;
    private readonly ManifestSprite?[] _topSprites;

    private TerrainSpriteSet(SpriteManifestPart face, ManifestSprite?[] faceSprites, SpriteManifestPart? top, ManifestSprite?[] topSprites)
    {
        _face = face;
        _faceSprites = faceSprites;
        _top = top;
        _topSprites = topSprites;

        var faceSets = face.Cells.Count / SpriteLinks.CellsPerSet(face.Linking);
        var topSets = top is null ? 0 : top.Cells.Count / SpriteLinks.CellsPerSet(top.Linking);
        VariantCount = face.VariantWeights is not null || top?.VariantWeights is not null
            ? SpriteLinks.PercentileRange
            : System.Math.Max(faceSets, topSets);
        TopRaisedRows = face.HeightRows();
    }

    /// <summary>How the face joins its neighbours: Horizontal for a wall's face, None for a floor or a plain face.</summary>
    public SpriteLinking FaceLinking => _face.Linking;

    /// <summary>How the top joins its neighbours; None when there is no top.</summary>
    public SpriteLinking TopLinking => _top?.Linking ?? SpriteLinking.None;

    /// <summary>How many variants a cell may roll: a percentile (100) when either part gives its variant sets chances, so each part maps the one roll through its own chances; otherwise the larger of the face's and the top's set counts.</summary>
    public int VariantCount { get; }

    /// <summary>How many rows above the wall's tile its top sits: the face's height (SpriteManifestPart.HeightRows), 1 for a face stretched to its tile.</summary>
    public int TopRaisedRows { get; }

    public bool HasTop => _top is not null;

    /// <summary>The link group a structure drawn with entry joins by: its top's for a wall, its own for anything else (SpriteManifestEntry.EffectiveTopLinkGroup / EffectiveLinkGroup).</summary>
    public string LinkGroup { get; private init; } = string.Empty;

    public static TerrainSpriteSet Resolve(SpriteManifestEntry entry)
    {
        var face = entry.Face;
        var faceSprites = new ManifestSprite?[face.Cells.Count];
        for (var index = 0; index < faceSprites.Length; index++)
        {
            faceSprites[index] = SpriteManifest.TryResolveCell(entry.Name, index, out var sprite) ? sprite : null;
        }

        var topSprites = new ManifestSprite?[entry.Top?.Cells.Count ?? 0];
        for (var index = 0; index < topSprites.Length; index++)
        {
            topSprites[index] = SpriteManifest.TryResolveTopCell(entry.Name, index, out var sprite) ? sprite : null;
        }

        return new TerrainSpriteSet(face, faceSprites, entry.Top, topSprites)
        {
            LinkGroup = entry.Top is null ? entry.EffectiveLinkGroup : entry.EffectiveTopLinkGroup,
        };
    }

    /// <summary>The face piece variant uses for mask -- null for the piece the set falls back to; mask is ignored for a plain (options) face.</summary>
    public bool TryGetFace(int variant, int? mask, out ManifestSprite sprite) => TryGet(_face, _faceSprites, variant, mask, out sprite);

    /// <summary>The top piece variant uses for mask -- null for the piece the set falls back to; false for a definition with no top.</summary>
    public bool TryGetTop(int variant, int? mask, out ManifestSprite sprite)
    {
        if (_top is not null)
        {
            return TryGet(_top, _topSprites, variant, mask, out sprite);
        }

        sprite = default;
        return false;
    }

    private static bool TryGet(SpriteManifestPart part, ManifestSprite?[] sprites, int variant, int? mask, out ManifestSprite sprite)
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
