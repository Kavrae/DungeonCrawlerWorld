using Engine.ECS.Systems;
using Game.Effects;
using Game.Modules.AbilityScores;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Grants the caster DodgingComponent for a Dexterity-scaled window (DodgeEffects.
/// ComputeWindowFrames) -- the sole effect on DodgeAction. Reads/writes SourceEntityId, not
/// TargetEntityId: unlike every other effect entry (DirectDamage, DirectHeal, ...), Dodge's benefit
/// always belongs to the caster specifically, never to "whoever the resolved target tile's occupant
/// turns out to be" -- ActionTargetingController.QueueActionActivation resolves Dodge's own
/// PendingActionActivationComponent.Selection at the caster's *own* current tile precisely so
/// this effect always finds at least the caster there, but that tile could still be shared with
/// another entity (a co-located Tiny/Phasing occupant) that must not also receive the grant.
///
/// Deliberately does not move the caster: DodgeAction's targeting (SingleTarget + Metric.Chebyshev,
/// Range 1) resolves to exactly one destination tile chosen at confirm time, and Presentation
/// (PlayerCommands) writes that step to MovementComponent.NextMapPosition once it sees this
/// activation's ActionActivatedEvent. DodgeAction's ReleasesActionLock frees the caster in the same
/// frame, so MovementSystem takes the step on its next pass (or leaves the entity in place if the
/// destination turns out occupied), with the same occupancy/wall validation normal movement has.
/// IEffectEntry only ever sees IMapQuery (read-only, mod-safe), never a map-mutating move
/// primitive, so keeping the actual move in Presentation (which already owns the player's
/// MovementComponent for ordinary movement) avoids widening that boundary for one action's
/// benefit. Falls back to WindowFrames (Dexterity 1's own value) if
/// AbilityScores/Dexterity isn't available, the same graceful-degradation shape
/// AbilityScoreTagBonus.Compute uses. Does nothing when the effect has no source entity (terrain,
/// an aura): there is no caster to dodge.
/// </summary>
public sealed record DodgeActivation : IEffectEntry
{
    public EffectOutcome Apply(in EffectContext context)
    {
        if (context.SourceEntityId is not { } sourceEntityId)
        {
            return EffectOutcome.NoEffect;
        }

        var windowFrames = DodgeEffects.WindowFrames;
        if (AbilityScoreQueries.TryGetComponent(context.Services.AbilityScores, sourceEntityId, AbilityScoreType.Dexterity, out var dexterity))
        {
            windowFrames = DodgeEffects.ComputeWindowFrames(dexterity.Total);
        }

        context.Services.ComponentManager.Merge(sourceEntityId, new DodgingComponent(FrameDeadline.After(context.Now, windowFrames)));
        return EffectOutcome.Applied;
    }
}
