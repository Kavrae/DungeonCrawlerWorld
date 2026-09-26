using Game.Modules.Core.Components;
using Game.Sprites;
using Microsoft.Xna.Framework;

namespace Game.Blueprints;

/// <summary>How every entity of one blueprint looks and is called, with each entity's variant chosen from its seed alone.</summary>
/// <remarks>
/// A pure function of (appearance, seed), read wherever an entity is drawn or named (MapViewQuery,
/// EntityNaming) rather than written to each entity: an entity holds a GlyphComponent,
/// SpriteComponent or DisplayTextComponent only when that one entity differs from its blueprint -- a
/// destroyed container, say. Variants are picked by hashing the seed, never by drawing from
/// BlueprintContext.Rolls, so they don't depend on what the blueprint rolled, and an entity that has
/// never been built looks exactly like the one it will be.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityAppearance
{
    private const uint NameSalt = 0x9E3779B9;
    private const uint SpriteSalt = 0x85EBCA6B;

    private readonly string? _name;
    private readonly IReadOnlyList<string> _displayNames;
    private readonly string _nameSuffix;

    internal EntityAppearance(string? name, IReadOnlyList<string> displayNames, IReadOnlyList<string> nameSuffixes, string description, string glyph, Color glyphColor, string? spriteName, IReadOnlyList<string> missing)
    {
        _name = name;
        _displayNames = displayNames;
        _nameSuffix = string.Join(' ', nameSuffixes);
        Description = description;
        Glyph = glyph;
        GlyphColor = glyphColor;
        SpriteName = spriteName;
        Missing = missing;
    }

    public string Description { get; }

    /// <summary>Empty when nothing declared one.</summary>
    public string Glyph { get; }

    public Color GlyphColor { get; }

    /// <summary>The SpriteManifest entry a cell is picked from, or null for glyph only.</summary>
    public string? SpriteName { get; }

    /// <summary>Whether the blueprint declares the entity's whole name, which nothing composed onto it -- a class, a suffix -- extends.</summary>
    public bool HasExplicitName => _name is not null;

    /// <summary>What the blueprint never declared -- a name, a description, a glyph, a sprite (or NoSprite) -- so it can't be spawned on its own. Empty for a complete appearance.</summary>
    public IReadOnlyList<string> Missing { get; }

    /// <summary>The name seed's entity gets: an explicit name if one was declared, else one of the display names followed by every class name and suffix. Empty when nothing names it.</summary>
    public string NameFor(uint seed)
    {
        if (_name is not null)
        {
            return _name;
        }

        var name = _displayNames.Count == 0 ? string.Empty : _displayNames[Variant(seed, NameSalt, _displayNames.Count)];
        return _nameSuffix.Length == 0 ? name
            : name.Length == 0 ? _nameSuffix
            : $"{name} {_nameSuffix}";
    }

    public bool TryGetSprite(uint seed, out SpriteComponent sprite)
    {
        var count = SpriteName is null ? 0 : SpriteManifest.CountCells(SpriteName);
        if (count == 0)
        {
            sprite = default;
            return false;
        }

        return SpriteManifest.TryGetCell(SpriteName!, Variant(seed, SpriteSalt, count), out sprite);
    }

    private static int Variant(uint seed, uint salt, int count)
    {
        var mixed = (seed ^ salt) * 0x9E3779B97F4A7C15UL;
        mixed ^= mixed >> 31;
        return (int)(mixed % (uint)count);
    }
}
