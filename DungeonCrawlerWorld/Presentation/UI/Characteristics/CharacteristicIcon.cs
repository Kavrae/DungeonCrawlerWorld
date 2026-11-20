using Engine.Math;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Game.Views;

namespace Presentation.UI.Characteristics;

/// <summary>One small icon in an inspected entity's characteristic row: a sprite by name else a glyph, or a drawn footprint, and its tooltip.</summary>
/// <param name="Footprint">Set for a drawn footprint, which then shows instead of SpriteName and Glyph.</param>
public readonly record struct CharacteristicIcon(string TooltipText, string? SpriteName = null, string Glyph = "", Vector2Byte? Footprint = null);

/// <summary>Which characteristic icons an entity shows, in order, and what each one says.</summary>
public static class CharacteristicIcons
{
    /// <summary>⁘ (U+2058, "four dot punctuation"): a swarm of specks, for Tiny.</summary>
    public const string TinyGlyph = "⁘";

    public const string TinySpriteName = "characteristic-size-tiny";

    public static IReadOnlyList<CharacteristicIcon> For(OccupantView occupant) =>
        [Size(occupant.Size, occupant.Kind)];

    /// <summary>Tiny's four dots, or the footprint drawn tile by tile, with the size category and footprint as its tooltip.</summary>
    public static CharacteristicIcon Size(Vector2Byte footprint, NonBlockingKind kind)
    {
        var category = EntitySizeCategories.Of(footprint, kind);
        var tooltipText = SizeTooltipText(category, footprint);

        return category == EntitySizeCategory.Tiny
            ? new CharacteristicIcon(tooltipText, TinySpriteName, TinyGlyph)
            : new CharacteristicIcon(tooltipText, Footprint: footprint);
    }

    public static string SizeTooltipText(EntitySizeCategory category, Vector2Byte footprint) =>
        $"Size : {category}\nFootprint : {footprint.X}x{footprint.Y}";
}
