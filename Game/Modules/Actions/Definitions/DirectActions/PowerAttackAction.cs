using Engine.Math;
using Game.Effects;
using Game.Effects.Entries;
using Engine.Utilities;
using Game.Modules.Actions.Activators;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.DirectActions;

/// <summary>Heavy melee attack -- a Delayed DirectAction: sets the shared ActionLock for its own 1s windup (telegraphed on the map, see Combat Overhaul: Dodge) before the effect applies, giving the target a real window to Dodge. Dodgeable and higher-damage than QuickAttack, its Immediate sibling. Staggering: a hit cancels the target's own windup and drops the player's buffered command -- the only action with that tag. No cooldown besides the shared lock itself.</summary>
public static class PowerAttackAction
{
    public static readonly Guid Id = new("2f4e6a8c-1d3b-4f5e-9a7c-6b8d0e2f4a6c");

    private static readonly ushort WindupFrames = GameTiming.FramesForSeconds(1f);

    public static ActionDefinition Build() => new(
        Id, "Power Attack", "PowerAttack", "P", Color.DarkRed,
        Tags: [GameTags.DeliveryMelee, GameTags.ActionAttack, GameTags.StatsAbilityScoreStrength, GameTags.TraitDodgeable, GameTags.TraitStaggering],
        Effects: [new Effect([new DirectDamage(MinFlatDamage: 36, MaxFlatDamage: 44)])],
        Activator: new DirectAction(
            new TargetingSpec(TargetShape.Adjacent, Range: 0, Modes: TargetingModes.GroundOnly),
            new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: WindupFrames, CooldownFrames: null)),
        Description: "A heavy, telegraphed strike -- a visible windup gives the target a real chance to Dodge before it lands, in exchange for far more damage than a Quick Attack. A hit staggers the target, interrupting any attack they are winding up.",
        Summary: "Slow, heavy, dodgeable melee attack");
}
