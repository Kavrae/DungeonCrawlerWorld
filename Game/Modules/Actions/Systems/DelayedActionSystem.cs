using Game.Effects;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions.Components;
using Game.Modules.Death.Components;
using Game.Modules.ProcessingTier;
using Game.World;

namespace Game.Modules.Actions.Systems;

/// <summary>
/// Resolves each windup, of an action or an item, on the exact frame it ends.
/// </summary>
/// <remarks>
/// The windup's end is a deadline carried by PendingWindupComponent itself, copied from the
/// shared ActionLockComponent when the action was queued, and the component sits on a timer wheel
/// keyed to it -- so this system touches only the actions actually resolving this frame, at any
/// processing tier, instead of visiting every pending entity to ask "is the lock 0 yet?"
/// (PendingWindupComponent.Count was measured over 10,000 map-wide during ordinary
/// NPC-vs-NPC combat, costing this system ~79ms of a 1000ms/sec budget before it was even tiered).
///
/// The old "stay on the same tiered cadence as ActionLockSystem so the two can't drift" invariant
/// this class used to defend is now structural: there is no second clock to drift, because the
/// lock and the pending action hold the same deadline value, and nothing ticks either of them.
/// MapWindow's charge-fill telegraph depends on that invariant (see PLAN-charge-attack-fill-
/// indicator.md's own Design section) and is strictly better served by it.
///
/// A cancelled action (right-click tap / Escape) simply removes the component; its wheel entry is
/// dropped as stale when the frame comes round (lazy cancellation, see PackedTimerWheel).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DelayedActionSystem : ISystem
{
    /// <summary>Drops a windup whose owner has just frozen: its target is either frozen too (untargetable across the seam) or simulated and long gone by the time the owner thaws, so resolving it later would land an attack nobody could react to.</summary>
    private void CancelWindupOnFreeze(int entityId, ProcessingTier.Components.ProcessingTierLevel tier)
    {
        if (!ProcessingTierQuery.IsSimulatedTier(tier))
        {
            _pendingActions.Remove(entityId);
        }
    }

    /// <summary>Every frame; the wheel only touches windups actually ending.</summary>
    public byte StripeCount => 1;

    private readonly PackedComponentPool<PendingWindupComponent> _pendingActions;
    private readonly EntityActions _actions;
    private readonly EffectServices _effectServices;
    private readonly ActionCatalog _actionCatalog;
    private readonly IMapQuery _mapQuery;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly PackedComponentPool<DodgingComponent> _dodgingEntities;
    private readonly ProcessingTierQuery _processingTiers;
    private readonly Toggles _toggles;
    private readonly WindupResolvers _windupResolvers;
    private readonly TargetResolution _targetResolution;
    private readonly List<Vector3Int> _targetTilesScratch = [];
    private readonly PackedTimerWheel<PendingWindupComponent> _wheel;

    // Cached once instead of passing the method group every Update -- an instance method group
    // conversion allocates a fresh delegate every evaluation.
    private readonly TimerFired<PendingWindupComponent> _resolve;

    public DelayedActionSystem(
        PackedComponentPool<PendingWindupComponent> pendingActions,
        EntityActions actions,
        EffectServices effectServices,
        ActionCatalog actionCatalog,
        IMapQuery mapQuery,
        PackedComponentPool<DodgingComponent> dodgingEntities,
        ProcessingTierQuery processingTiers,
        SimulationScope simulationScope,
        ProcessingTierEvents processingTierEvents,
        Toggles toggles,
        WindupResolvers windupResolvers,
        TargetResolution targetResolution)
    {
        _effectServices = effectServices;
        _pendingActions = pendingActions;
        _actions = actions;
        _actionCatalog = actionCatalog;
        _mapQuery = mapQuery;
        _deadEntities = effectServices.DeadEntities;
        _dodgingEntities = dodgingEntities;
        _processingTiers = processingTiers;
        _toggles = toggles;
        _windupResolvers = windupResolvers;
        _targetResolution = targetResolution;
        _resolve = Resolve;
        _wheel = new PackedTimerWheel<PendingWindupComponent>(pendingActions, simulationScope);

        processingTierEvents.TierChanged += CancelWindupOnFreeze;
    }

    public void Update(EngineTime time, byte stripeIndex) => _wheel.Tick(time.FrameCount, _resolve);

    /// <summary>Always returns true -- a windup resolves exactly once, so it is removed either way (see TimerFired's contract).</summary>
    /// <remarks>
    /// An item's windup goes to the resolver Inventory registered. A toggle action's goes on, unless it
    /// already is; its activation effects were applied when the windup began. Any other action applies
    /// its effects to its targets, resolved now (TargetResolution), so a Target-mode windup lands where its target is when it ends.
    /// </remarks>
    private bool Resolve(int entityId, PendingWindupComponent pending, long now)
    {
        // A corpse can't finish a windup. Removing it (rather than just skipping) is what keeps a
        // dead entity from carrying a stale pending windup forever -- nothing else clears it.
        if (_deadEntities.Has(entityId))
        {
            return true;
        }

        if (pending.Activatable.Kind == ActivatableKind.Item)
        {
            _windupResolvers.ItemResolver?.Resolve(entityId, in pending, now);
            return true;
        }

        if (!_actions.TryGetEffectiveAction(entityId, pending.Activatable.ActionId, out var action))
        {
            return true;
        }

        if (action.Toggle is not null)
        {
            var owner = ActivatableReference.Action(action.Id);
            if (!_toggles.IsOn(entityId, owner))
            {
                _effectServices.EventBus.Publish(new ActionActivatedEvent(entityId, action.Id));
                _toggles.TurnOn(entityId, owner, action, now, placesAtHolderTile: pending.Selection.Mode == TargetingMode.Ground);
            }

            return true;
        }

        var resolved = _targetResolution.Resolve(entityId, action.Activator.Targeting, pending.Selection, _targetTilesScratch);
        ActionEffectResolver.Apply(action, entityId, _targetTilesScratch, resolved, _effectServices, _mapQuery, now, _dodgingEntities, _processingTiers);
        return true;
    }
}
