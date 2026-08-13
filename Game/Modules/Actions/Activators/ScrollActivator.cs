using Engine.Math;

namespace Game.Modules.Actions.Activators;

/// <summary>
/// Item-triggered activator for a one-time-use scroll -- no mana (items never cost mana today),
/// Immediate timing by convention (constructed the same way PotionActivator always is). SpellId
/// names the spell this scroll is a scroll-form of: ScrollMasteryEffects.RecordUsage looks it up
/// in ActionCatalog first, and only synthesizes a fresh ActionDefinition from this scroll's own
/// ItemDefinition if no spell is registered under that id yet -- see its own doc comment. Range/
/// AreaSize on Targeting are the item's unscaled base values; ScrollScalingEffects is what scales
/// them (and any duration the effect carries) with the caster's Intelligence at use time.
/// </summary>
public sealed record ScrollActivator(TargetingSpec Targeting, ActionTiming Timing, Guid SpellId) : IActionActivator;
