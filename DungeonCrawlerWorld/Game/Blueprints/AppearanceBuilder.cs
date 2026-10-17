using Microsoft.Xna.Framework;

namespace Game.Blueprints;

/// <summary>Folds the appearance facets of a build order into one <see cref="EntityAppearance"/>, each set field replacing what came before.</summary>
internal sealed class AppearanceBuilder
{
    private readonly List<string> _nameSuffixes = [];
    private string? _name;
    private IReadOnlyList<string>? _displayNames;
    private string? _description;
    private string? _glyph;
    private Color? _glyphColor;
    private string? _spriteName;

    public void Apply(AppearanceFacet facet)
    {
        _name = facet.Name ?? _name;
        _displayNames = facet.DisplayNames ?? _displayNames;
        _description = facet.Description ?? _description;
        _glyph = facet.Glyph ?? _glyph;
        _glyphColor = facet.GlyphColor ?? _glyphColor;
        _spriteName = facet.SpriteName ?? _spriteName;

        if (facet.NameSuffix is { } suffix)
        {
            AddNameSuffix(suffix);
        }
    }

    public void AddNameSuffix(string suffix) => _nameSuffixes.Add(suffix);

    public EntityAppearance Build()
    {
        var missing = new List<string>();
        if (_name is null && (_displayNames is null || _displayNames.Count == 0))
        {
            missing.Add("a name");
        }

        if (_description is null)
        {
            missing.Add("a description");
        }

        if (_glyph is null || _glyphColor is null)
        {
            missing.Add("a glyph and its color");
        }

        if (_spriteName is null)
        {
            missing.Add("a sprite (or AppearanceFacet.NoSprite)");
        }

        return new EntityAppearance(
            _name,
            _displayNames ?? [],
            [.. _nameSuffixes],
            _description ?? string.Empty,
            _glyph ?? string.Empty,
            _glyphColor ?? Color.White,
            string.IsNullOrEmpty(_spriteName) ? null : _spriteName,
            missing);
    }
}
