using Microsoft.Xna.Framework;

namespace Game.Modules.Actions;

/// <summary>
/// Shared, catalog-level content shape for anything with a name/glyph/tags that produces
/// ActionEffects -- ItemDefinition (Game.Modules.Inventory) and ActionDefinition both derive from
/// this instead of duplicating the same six presentation/effect fields. SpriteName/GlyphColor
/// follow the sprite-first, glyph-as-fallback convention (see GlyphComponent's own doc comment).
/// Summary is a short, concrete statement of exact effect meant to be read at a glance in a small
/// window (see HotbarContent.TryGetSlotSummary); Description is longer flavor/detail text for
/// future, larger text boxes elsewhere -- deliberately separate fields, not one reused for both.
/// Effects lives here (not on IActionActivator) so a future passive, on-equip, or condition-
/// triggered effect can reuse the same list/vocabulary without needing to fake a Targeting/Timing
/// it doesn't have just to qualify as an activator.
/// </summary>
public abstract record ActivatableDefinition(
    Guid Id,
    string Name,
    string? SpriteName,
    string Glyph,
    Color GlyphColor,
    IReadOnlyList<Tag> Tags,
    IReadOnlyList<ActionEffect> Effects,
    string Description = "",
    string Summary = "");
