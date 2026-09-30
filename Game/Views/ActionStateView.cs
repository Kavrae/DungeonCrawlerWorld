using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;

namespace Game.Views;

/// <summary>What gates an entity's next action -- its action lock, mana and potion cooldown -- and the windup it's in, if any.</summary>
/// <param name="localTierRoster">Scopes CopyLocalPendingDelayedActions to the Local tier; null (a test without tiers) keeps every pending windup.</param>
public sealed class ActionStateView(ComponentManager componentManager, LocalTierRoster? localTierRoster)
{
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
    private readonly PackedComponentPool<ManaComponent> _mana = componentManager.GetPackedPool<ManaComponent>();
    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns = componentManager.GetPackedPool<PotionCooldownComponent>();
    private readonly PackedComponentPool<PendingDelayedActionComponent> _pendingDelayedActions = componentManager.GetPackedPool<PendingDelayedActionComponent>();

    public bool TryGetActionLock(int entityId, out ActionLockComponent actionLock) => _actionLocks.TryGetReadonly(entityId, out actionLock);

    /// <inheritdoc cref="ActionLockGate.IsBlocked"/>
    public bool IsActionLocked(int entityId, long now) => ActionLockGate.IsBlocked(_actionLocks, entityId, now);

    public bool TryGetMana(int entityId, out ManaComponent mana) => _mana.TryGetReadonly(entityId, out mana);

    public bool TryGetPotionCooldown(int entityId, out PotionCooldownComponent potionCooldown) => _potionCooldowns.TryGetReadonly(entityId, out potionCooldown);

    /// <summary>The Delayed action entityId is winding up, if any.</summary>
    public bool TryGetPendingDelayedAction(int entityId, out PendingDelayedActionComponent pending) => _pendingDelayedActions.TryGetReadonly(entityId, out pending);

    /// <summary>Replaces destination's contents with every windup in the Local tier, the player's always included.</summary>
    /// <remarks>
    /// Walks whichever is smaller this frame: the pending windups or the Local roster. Mid-brawl the windups run to
    /// five figures map-wide against a roster of about a thousand; during quiet exploration almost nothing winds up.
    /// Both produce the same entries; only the number of probes differs. Walking the windups alone and rejecting
    /// non-Local ones once cost ~10,000 scattered tier reads on every draw. The player is added whatever its tier:
    /// it is the camera anchor, and the roster (driven by movers) isn't guaranteed to hold it.
    /// </remarks>
    public void CopyLocalPendingDelayedActions(int playerEntityId, List<(int EntityId, PendingDelayedActionComponent Pending)> destination)
    {
        destination.Clear();

        if (localTierRoster is null || _pendingDelayedActions.Count <= localTierRoster.Count)
        {
            var entityIds = _pendingDelayedActions.EntityIds;
            var components = _pendingDelayedActions.Components;
            for (var denseIndex = 0; denseIndex < _pendingDelayedActions.Count; denseIndex++)
            {
                var entityId = entityIds[denseIndex];
                if (entityId == playerEntityId || localTierRoster is null || localTierRoster.IsLocal(entityId))
                {
                    destination.Add((entityId, components[denseIndex]));
                }
            }

            return;
        }

        if (_pendingDelayedActions.TryGetReadonly(playerEntityId, out var playerPending))
        {
            destination.Add((playerEntityId, playerPending));
        }

        foreach (var entityId in localTierRoster.LocalEntityIds)
        {
            if (entityId != playerEntityId && _pendingDelayedActions.TryGetReadonly(entityId, out var pending))
            {
                destination.Add((entityId, pending));
            }
        }
    }
}
