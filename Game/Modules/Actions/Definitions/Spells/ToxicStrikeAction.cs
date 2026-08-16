using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.Spells;

/// <summary>Ranged DoT-only attack spell, free to cast today (ManaCost 0) -- see ActionDefinition/SpellActivator's own doc comments for the shape.</summary>
public static class ToxicStrikeAction
{
    public static readonly Guid Id = new("b8729e94-aee0-42d4-bda2-9b323afd3134");

    public static ActionDefinition Build() => new(
        Id, "Toxic Strike", null, "t", Color.Purple,
        Tags: [Tag.Ranged, Tag.Attack, Tag.Spell],
        Effects: [new ActionEffect([
            new StatusEffectGrant(StatusEffectType.Poison, StackCount: 10),
            new StatusEffectGrant(StatusEffectType.Burning, StackCount: 6),
        ])],
        Activator: new SpellActivator(
            new TargetingSpec(TargetShape.SingleTarget, Range: 15),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)),
        Description: "A single target ranged attack that deals no direct damage of its own, instead drenching the target in a toxic, smoldering residue -- 10 stacks of Poison and 6 stacks of Burning, left to tick on their own.",
        Summary: "Inflicts 10 stacks of Poison and 6 stacks of Burning.");
}
