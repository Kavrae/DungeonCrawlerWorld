using Game.Modules.Actions;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory;

/// <summary>Represents the definition of an item in the game.</summary>
/// <param name="Id">The unique identifier for the item.</param>
/// <param name="Name">The name of the item.</param>
/// <param name="SpriteName">The name of the sprite representing the item. Falls back to the Glyph if not provided.</param>
/// <param name="Glyph">The fallback character used to represent the item in the UI.</param>
/// <param name="GlyphColor">The color of the item's glyph.</param>
/// <param name="Tags">An optional list of tags associated with the item.</param>
/// <param name="Effects">An optional list of effects triggered by the item.</param>
/// <param name="Description">The full description of the item.</param>
/// <param name="Summary">A brief summary of the item.</param>
/// <param name="MaxStackSize">The maximum stack size for the item in a single inventory or hotkey slot.</param>
/// <param name="Activator">The optional activator type for the item that determines how the effects are activated, if any.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record ItemDefinition(
    Guid Id,
    string Name,
    string? SpriteName,
    string Glyph,
    Color GlyphColor,
    IReadOnlyList<Tag> Tags,
    IReadOnlyList<ActionEffect> Effects,
    string Description = "",
    string Summary = "",
    int? MaxStackSize = null,
    IActionActivator? Activator = null)
    : ActivatableDefinition(Id, Name, SpriteName, Glyph, GlyphColor, Tags, Effects, Description, Summary);
