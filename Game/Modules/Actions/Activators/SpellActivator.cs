using Engine.Math;

namespace Game.Modules.Actions.Activators;

/// <summary>Action activator for casting spells.</summary>
/// <param name="Targeting">The targeting specification for the spell.</param>
/// <param name="Timing">The timing specification for the spell.</param>
/// <param name="ManaCost">The mana cost for casting the spell.</param>
/// <cleanupVersion>1</cleanupVersion>
public sealed record SpellActivator(TargetingSpec Targeting, ActionTiming Timing, ushort ManaCost = 0) : IActionActivator
{
    /// <summary>Returns activator's ManaCost if it's a SpellActivator, 0 otherwise -- the single place every "is this affordable" check reads mana cost from, so a DirectAction/PotionActivator never needs its own always-zero stand-in field.</summary>
    public static ushort ManaCostOf(IActionActivator activator) => activator is SpellActivator spell ? spell.ManaCost : (ushort)0;
}
