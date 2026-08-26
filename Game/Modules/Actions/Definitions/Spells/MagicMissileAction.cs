using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.Spells;

/// <summary>Basic single-target ranged attack spell -- see ActionDefinition/SpellActivator's own doc comments for the shape.</summary>
public static class MagicMissileAction
{
    public static readonly Guid Id = new("2b6d4f8a-1c3e-4a5d-9f7b-6e8c1a3d5f9b");
    public const ushort ManaCost = 5;

    public static ActionDefinition Build() => new(
        Id, "Magic Missile", "Magic Missile", "m", Color.Black,
        Tags: [Tag.Ranged, Tag.Attack, Tag.Spell],
        Effects: [new ActionEffect([new DirectDamage(MinAmount: 20, MaxAmount: 25, TargetBodyPartType: BodyPartType.Head)])],
        Activator: new SpellActivator(
            new TargetingSpec(TargetShape.SingleTarget, Range: 20),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null),
            ManaCost),
        Description: "A basic single target ranged attack spell that shoots hot laser bolts from the caster's eyes, one bolt after another.",
        Summary: "Single target ranged attack.");
}
