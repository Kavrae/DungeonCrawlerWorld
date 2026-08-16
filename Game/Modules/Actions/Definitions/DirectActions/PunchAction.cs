using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.DirectActions;

/// <summary>Basic melee attack -- a DirectAction (no mana, no special mechanic), primarily modified by the Bare Knuckle skill. See ActionDefinition/DirectAction's own doc comments for the shape.</summary>
public static class PunchAction
{
    public static readonly Guid Id = new("7a1c3e5f-9b2d-4c6a-8e1f-3d5b7a9c2e4f");

    public static ActionDefinition Build() => new(
        Id, "Punch", "Punch", "p", Color.Black,
        Tags: [Tag.Melee, Tag.Unarmed, Tag.Attack, Tag.Strength],
        Effects: [new ActionEffect([new DirectDamage(MinAmount: 18, MaxAmount: 22)])],
        Activator: new DirectAction(
            new TargetingSpec(TargetShape.Adjacent, Range: 0),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)),
        Description: "It doesn't get any more simple than this. Just keep hitting the target with your bare fists until they stop moving. This action is primarily modified by the Bare Knuckle skill.",
        Summary: "Basic melee attack");
}
