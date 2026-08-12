using Engine.Math;

namespace Game.Modules.Actions.Activators;

/// <summary>
/// Granted-action activator with an optional mana cost -- ActionActivationSystem gates/spends
/// ManaCost at cast time (ManaCost &lt;= 0, the default, is always free). Used for every granted
/// action that isn't a bare DirectAction, whether or not it actually costs mana today (e.g. Toxic
/// Strike is thematically a spell but currently free) -- "spell" here means "granted, mana-capable
/// activation," not literally every Tag.Spell-tagged action.
/// </summary>
public sealed record SpellActivator(TargetingSpec Targeting, ActionTiming Timing, short ManaCost = 0) : IActionActivator
{
    /// <summary>Returns activator's ManaCost if it's a SpellActivator, 0 otherwise -- the single place every "is this affordable" check reads mana cost from, so a DirectAction/PotionActivator never needs its own always-zero stand-in field.</summary>
    public static short ManaCostOf(IActionActivator activator) => activator is SpellActivator spell ? spell.ManaCost : (short)0;
}
