using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.StatModifiers.Components;

namespace Game.Views;

/// <summary>What gates an entity's next action -- what blocks it, its action lock, mana, cooldowns and potion cooldown -- and the windup it's in, if any.</summary>
/// <param name="localTierRoster">Scopes CopyLocalPendingWindups to the Local tier; null (a test without tiers) keeps every pending windup.</param>
/// <param name="effectServices">Asks and prices activation effects.</param>
/// <param name="toggles">Answers which toggles are on; null (a test without toggles) reads every toggle as off.</param>
/// <param name="simulationClock">The frame activation effects are asked on; null reads frame 0.</param>
public sealed class ActionStateView(ComponentManager componentManager, EntityActions entityActions, ItemCatalog itemCatalog, LocalTierRoster? localTierRoster, EffectServices effectServices, Toggles? toggles = null, SimulationClock? simulationClock = null)
{
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
    private readonly PackedComponentPool<ManaComponent> _mana = componentManager.GetPackedPool<ManaComponent>();
    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns = componentManager.GetPackedPool<PotionCooldownComponent>();
    private readonly PackedComponentPool<PendingWindupComponent> _pendingWindups = componentManager.GetPackedPool<PendingWindupComponent>();
    private readonly PackedComponentPool<MeleeDisabledComponent> _meleeDisabled = componentManager.GetPackedPool<MeleeDisabledComponent>();
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();

    public bool TryGetActionLock(int entityId, out ActionLockComponent actionLock) => _actionLocks.TryGetReadonly(entityId, out actionLock);

    /// <inheritdoc cref="ActionLockGate.IsBlocked"/>
    public bool IsActionLocked(int entityId, long now) => ActionLockGate.IsBlocked(_actionLocks, entityId, now);

    public bool TryGetMana(int entityId, out ManaComponent mana) => _mana.TryGetReadonly(entityId, out mana);

    public bool TryGetPotionCooldown(int entityId, out PotionCooldownComponent potionCooldown) => _potionCooldowns.TryGetReadonly(entityId, out potionCooldown);

    /// <inheritdoc cref="ActivationQueries.GetBlocker"/>
    /// <remarks>Reads the entity's effective action. An action the entity doesn't have is None: a binding problem, not a reason to show.</remarks>
    public ActivationBlocker GetActionBlocker(int entityId, Guid actionId) =>
        entityActions.TryGetEffectiveAction(entityId, actionId, out var action)
            ? ActivationQueries.GetBlocker(entityId, action, action.Activator, IsActionToggledOn(entityId, actionId), _meleeDisabled, effectServices, Now)
            : ActivationBlocker.None;

    /// <inheritdoc cref="ActivationQueries.GetBlocker"/>
    /// <remarks>Reads the stack's effective item. A stack the entity doesn't hold is None.</remarks>
    public ActivationBlocker GetItemBlocker(int entityId, uint stackInstanceId) =>
        InventoryQueries.TryFindByStackInstanceId(_inventoryStacks, entityId, stackInstanceId, out var stack) &&
        InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item)
            ? ActivationQueries.GetBlocker(entityId, item, item.Activator, item.Activator is ToggleItemActivator { IsToggledOn: true }, _meleeDisabled, effectServices, Now)
            : ActivationBlocker.None;

    /// <inheritdoc cref="ActivationCosts.Of"/>
    /// <remarks>Reads the entity's effective action. An action the entity doesn't have costs nothing, and neither does one that is on: turning a toggle off takes nothing.</remarks>
    public ActivationCostTotals ActionCostsOf(int entityId, Guid actionId) =>
        entityActions.TryGetEffectiveAction(entityId, actionId, out var action) && !(action.Toggle is not null && IsActionToggledOn(entityId, actionId))
            ? ActivationCosts.Of(effectServices, entityId, action, Now)
            : default;

    /// <inheritdoc cref="ActivationCosts.Of"/>
    /// <remarks>Reads the stack's effective item. A stack the entity doesn't hold costs nothing, and neither does a lit unit: putting it out takes nothing.</remarks>
    public ActivationCostTotals ItemCostsOf(int entityId, uint stackInstanceId) =>
        InventoryQueries.TryFindByStackInstanceId(_inventoryStacks, entityId, stackInstanceId, out var stack) &&
        InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item) &&
        item.Activator is not ToggleItemActivator { IsToggledOn: true }
            ? ActivationCosts.Of(effectServices, entityId, item, Now)
            : default;

    /// <summary>Whether entityId has the toggle action actionId on.</summary>
    public bool IsActionToggledOn(int entityId, Guid actionId) => toggles is not null && toggles.IsOn(entityId, ActivatableReference.Action(actionId));

    private long Now => simulationClock?.CurrentFrame ?? 0;

    /// <inheritdoc cref="ActivationQueries.FramesUntilReady"/>
    /// <remarks>Reads the entity's effective action. An action the entity doesn't have is 0.</remarks>
    public int FramesUntilReady(int entityId, Guid actionId, long now) =>
        entityActions.TryGetEffectiveAction(entityId, actionId, out var action)
            ? ActivationQueries.FramesUntilReady(entityId, action, entityActions, _actionLocks, now)
            : 0;

    /// <summary>The Delayed action entityId is winding up, if any.</summary>
    public bool TryGetPendingWindup(int entityId, out PendingWindupComponent pending) => _pendingWindups.TryGetReadonly(entityId, out pending);

    /// <summary>Replaces destination's contents with every windup in the Local tier, the player's always included.</summary>
    /// <remarks>
    /// Walks whichever is smaller this frame: the pending windups or the Local roster. Mid-brawl the windups run to
    /// five figures map-wide against a roster of about a thousand; during quiet exploration almost nothing winds up.
    /// Both produce the same entries; only the number of probes differs. Walking the windups alone and rejecting
    /// non-Local ones once cost ~10,000 scattered tier reads on every draw. The player is added whatever its tier:
    /// it is the camera anchor, and the roster (driven by movers) isn't guaranteed to hold it.
    /// </remarks>
    public void CopyLocalPendingWindups(int playerEntityId, List<(int EntityId, PendingWindupComponent Pending)> destination)
    {
        destination.Clear();

        if (localTierRoster is null || _pendingWindups.Count <= localTierRoster.Count)
        {
            var entityIds = _pendingWindups.EntityIds;
            var components = _pendingWindups.Components;
            for (var denseIndex = 0; denseIndex < _pendingWindups.Count; denseIndex++)
            {
                var entityId = entityIds[denseIndex];
                if (entityId == playerEntityId || localTierRoster is null || localTierRoster.IsLocal(entityId))
                {
                    destination.Add((entityId, components[denseIndex]));
                }
            }
        }
        else
        {
            if (_pendingWindups.TryGetReadonly(playerEntityId, out var playerPending))
            {
                destination.Add((playerEntityId, playerPending));
            }

            foreach (var entityId in localTierRoster.LocalEntityIds)
            {
                if (entityId != playerEntityId && _pendingWindups.TryGetReadonly(entityId, out var pending))
                {
                    destination.Add((entityId, pending));
                }
            }
        }
    }
}
