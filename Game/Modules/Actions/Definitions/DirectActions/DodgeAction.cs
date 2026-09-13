using Engine.Math;
using Engine.Utilities;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Actions.Definitions.DirectActions;

/// <summary>
/// Avoid an incoming attack -- a FreeCast DirectAction (bypasses the shared lock entirely, per
/// ActionTimingCategory's own doc comment), gated instead by its own flat CooldownFrames (4s) so it
/// can't be spammed as a safer form of movement (see TODO.md's Combat Overhaul: Dodge). Targeting is
/// SingleTarget + Metric.Chebyshev at Range 1 -- "pick exactly one tile out of the caster's own 3x3
/// block" (self, or one of the 8 neighbors), resolved at confirm time by Presentation
/// (ActionTargetingController): same key/click-on-self dodges in place, a directional key or click
/// on a neighbor tile queues a move there through the same MovementComponent.NextMapPosition path
/// ordinary movement uses (or in place if that tile turns out occupied by the time MovementSystem
/// gets to it -- see DodgeActivation's own doc comment for why the actual relocation is queued in
/// Presentation, not applied here). The only effect here is DodgeActivation's own DodgingComponent
/// grant, which fires the instant this FreeCast action resolves regardless of whether the queued
/// move has actually completed yet.
/// </summary>
public static class DodgeAction
{
    public static readonly Guid Id = new("9d1f3b5a-7c9e-4a1d-8f3b-5a7c9e1d3f5a");

    private static readonly ushort CooldownFrames = GameTiming.FramesForSeconds(4f);

    public static ActionDefinition Build() => new(
        Id, "Dodge", "Dodge", "d", Color.LightGreen,
        Tags: [Tag.Self],
        Effects: [new ActionEffect([new DodgeActivation()])],
        Activator: new DirectAction(
            new TargetingSpec(TargetShape.SingleTarget, Range: 1, Metric: DistanceMetric.Chebyshev),
            new ActionTiming(ActionTimingCategory.FreeCast, CooldownFrames: CooldownFrames)),
        Description: "Briefly become immune to dodgeable attacks, optionally moving to an adjacent tile in the process. Has its own 4 second cooldown, so it can't be used as a safer form of movement.",
        Summary: "Briefly dodge dodgeable attacks");
}
