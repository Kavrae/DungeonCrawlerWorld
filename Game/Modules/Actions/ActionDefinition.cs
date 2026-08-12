using Microsoft.Xna.Framework;

namespace Game.Modules.Actions;

/// <summary>
/// Catalog-level data for one action
/// </summary>
/// <remarks>
/// Looked up by Id from ActionCatalog. Same abstraction level as Game.Modules.Inventory.
/// ItemDefinition (both derive from ActivatableDefinition) except for stacking/equipment concerns,
/// which don't apply to an action. Activator is required (unlike ItemDefinition's nullable one) --
/// an Action's whole point is to be activated. Per-entity state (granted damage override,
/// remaining cooldown) lives on ActionInstanceComponent instead.
/// </remarks>
public sealed record ActionDefinition(
    Guid Id,
    string Name,
    string? SpriteName,
    string Glyph,
    Color GlyphColor,
    IReadOnlyList<Tag> Tags,
    IReadOnlyList<ActionEffect> Effects,
    IActionActivator Activator,
    string Description = "",
    string Summary = "")
    : ActivatableDefinition(Id, Name, SpriteName, Glyph, GlyphColor, Tags, Effects, Description, Summary);
