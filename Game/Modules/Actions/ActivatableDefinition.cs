using Engine.Tags;
using Game.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions;

/// <summary>
/// Shared, catalog-level content shape for anything with a name/glyph/tags that produces
/// Effects -- ItemDefinition (Game.Modules.Inventory) and ActionDefinition both derive from
/// this instead of duplicating the same six presentation/effect fields. SpriteName/GlyphColor
/// follow the sprite-first, glyph-as-fallback convention (see GlyphComponent's own doc comment).
/// Summary is a short, concrete statement of exact effect meant to be read at a glance in a small
/// window (see HotbarContent.TryGetSlotSummary); Description is longer flavor/detail text for
/// future, larger text boxes elsewhere -- deliberately separate fields, not one reused for both.
/// Effects lives here (not on IActionActivator) so a future passive, on-equip, or condition-
/// triggered effect can reuse the same list/vocabulary without needing to fake a Targeting/Timing
/// it doesn't have just to qualify as an activator.
/// </summary>
/// <remarks>
/// Tags holds the tags a definition declares plus whatever its activator implies (see
/// IActionActivator.ImpliedTags): ActionDefinition and ItemDefinition add those at construction. A
/// with-expression that swaps the activator for another kind therefore keeps the old kind's implied tags.
/// Toggle makes the definition a toggle, whatever its activator: the activator still says how it is
/// triggered, and Effects become what is held while it is on (see ToggleSpec).
/// </remarks>
public abstract record ActivatableDefinition(
    Guid Id,
    string Name,
    string? SpriteName,
    string Glyph,
    Color GlyphColor,
    GameplayTagSet Tags,
    IReadOnlyList<Effect> Effects,
    string Description = "",
    string Summary = "",
    ToggleSpec? Toggle = null)
{
    /// <summary>What using this does to whoever uses it, applied before anything else it does: a cost (ManaDrain), or anything else an effect can do to the user.</summary>
    /// <remarks>
    /// Asked first and all or nothing (ActivationEffectsApplier): a use whose activation effects can't all be
    /// applied is blocked, with the first refusal's reason as the blocker. Applied with source and target both
    /// the user when the use goes ahead -- a Delayed one's when its windup starts, and never given back if it is
    /// cancelled. Turning a toggle off is not a use: it takes nothing.
    /// </remarks>
    public IReadOnlyList<Effect> ActivationEffects { get; init; } = [];
}
