using Microsoft.Xna.Framework;

namespace Game.Blueprints;

/// <summary>What a definition says about how an entity built from it looks and is called; every field it leaves null is left to the rest of the build order.</summary>
/// <remarks>
/// Resolved into one <see cref="EntityAppearance"/> per blueprint (see BlueprintRegistry): fields are
/// applied in build order, each set field replacing what came before, except that a second race's
/// appearance is ignored -- a hybrid looks like its first race. A composite overrides only what it
/// sets, so GoblinEngineer can change a goblin's description without restating its glyph.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed record AppearanceFacet
{
    /// <summary>The <see cref="SpriteName"/> that declares "glyph only" -- a sprite deliberately absent rather than not yet decided.</summary>
    public const string NoSprite = "";

    /// <summary>The entity's whole name, replacing the one its race, classes and suffixes would compose.</summary>
    public string? Name { get; init; }

    /// <summary>Appended to the composed name after what came before it -- the way a class's name is.</summary>
    public string? NameSuffix { get; init; }

    /// <summary>The names an entity is given one of, picked by its seed.</summary>
    public IReadOnlyList<string>? DisplayNames { get; init; }

    public string? Description { get; init; }

    public string? Glyph { get; init; }

    public Color? GlyphColor { get; init; }

    /// <summary>The SpriteManifest entry an entity draws a cell of, picked by its seed, or <see cref="NoSprite"/>.</summary>
    public string? SpriteName { get; init; }
}
