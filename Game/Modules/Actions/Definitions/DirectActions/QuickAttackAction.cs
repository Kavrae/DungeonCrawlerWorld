using Engine.Math;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.DirectActions;

/// <summary>Basic melee attack -- a DirectAction (no mana, no special mechanic), primarily modified by the Bare Knuckle skill. Immediate (no windup/telegraph), but still Dodgeable: a player who predicts it or reacts with good timing can dodge it blind, per Combat Overhaul: Dodge. See ActionDefinition/DirectAction's own doc comments for the shape.</summary>
public static class QuickAttackAction
{
    public static readonly Guid Id = new("7a1c3e5f-9b2d-4c6a-8e1f-3d5b7a9c2e4f");

    public static ActionDefinition Build() => new(
        Id, "Quick Attack", "QuickAttack", "q", Color.Black,
        Tags: [Tag.Melee, Tag.Unarmed, Tag.Attack, Tag.Strength, Tag.Dodgeable],
        Effects: [new ActionEffect([new DirectDamage(MinFlatDamage: 18, MaxFlatDamage: 22)])],
        Activator: new DirectAction(
            new TargetingSpec(TargetShape.Adjacent, Range: 0),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)),
        Description: "A fast, low-damage strike with no windup -- no target-shape telegraph appears before it lands, but a well-timed or predicted Dodge can still avoid it.",
        Summary: "Fast, low-damage melee attack");
}
