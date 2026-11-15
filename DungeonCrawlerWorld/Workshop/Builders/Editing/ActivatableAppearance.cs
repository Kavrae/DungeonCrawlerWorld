using Game.Modules.Actions;

namespace Builders.Editing;

/// <summary>How an action or item looks, and where its record holds that look -- for the warning that two records look the same.</summary>
public static class ActivatableAppearance
{
    public static ContentAppearanceFields Fields { get; } = new(ActivatableContent.GlyphKey, ActivatableContent.SpriteNameKey);

    public static ContentAppearance Of(ActivatableDefinition definition) =>
        new(definition.Id, definition.Name, definition.Glyph, definition.GlyphColor.PackedValue, definition.SpriteName);
}
