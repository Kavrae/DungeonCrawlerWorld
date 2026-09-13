using Engine.ECS.Systems;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Grants the caster DodgingComponent for a Dexterity-scaled window (DodgeEffects.
/// ComputeWindowFrames) -- the sole effect on DodgeAction. Reads/writes SourceEntityId, not
/// TargetEntityId: unlike every other effect entry (DirectDamage, DirectHeal, ...), Dodge's benefit
/// always belongs to the caster specifically, never to "whoever the resolved target tile's occupant
/// turns out to be" -- ActionTargetingController.QueueActionActivation resolves Dodge's own
/// PendingActionActivationComponent.TargetTiles against the caster's *own* current tile precisely so
/// this effect always finds at least the caster there, but that tile could still be shared with
/// another entity (a co-located Tiny/Phasing occupant) that must not also receive the grant.
///
/// Deliberately does not move the caster: DodgeAction's targeting (SingleTarget + Metric.Chebyshev,
/// Range 1) resolves to exactly one destination tile chosen at confirm time, and Presentation
/// (ActionTargetingController.TryRelocateForDodge) queues that relocation through the same
/// MovementComponent.NextMapPosition path ordinary WASD movement uses -- MovementSystem then applies
/// it (or leaves the entity in place if the destination turns out occupied, TODO.md's own "dodge in
/// place if occupied" fallback) on its own later pass, using the exact same occupancy/wall
/// validation normal movement already has. IActionEffectEntry only ever sees IMapQuery (read-only,
/// mod-safe), never a map-mutating move primitive, so keeping the actual move in Presentation
/// (which already owns the player's MovementComponent for ordinary movement) avoids widening that
/// boundary for one action's benefit. Falls back to WindowFrames (Dexterity 1's own value) if
/// AbilityScores/Dexterity isn't available, the same graceful-degradation shape
/// AbilityScoreTagBonus.Compute uses.
/// </summary>
public sealed record DodgeActivation : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        var windowFrames = DodgeEffects.WindowFrames;
        if (context.AbilityScores is { } abilityScores &&
            AbilityScoreQueries.TryGetComponent(abilityScores, context.SourceEntityId, AbilityScoreType.Dexterity, out var dexterity))
        {
            windowFrames = DodgeEffects.ComputeWindowFrames(dexterity.Total);
        }

        context.ComponentManager.Merge(context.SourceEntityId, new DodgingComponent(FrameDeadline.After(context.Now, windowFrames)));
    }
}
