using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Components;
using Game.Blueprints;
using Game.Spawning;

namespace Game.Modules.Actions;

/// <summary>
/// The actions an entity can use, and their cooldowns: what its blueprint grants every entity of it
/// (ActionGrant, shared), what the parts applied to it since grant, plus what was granted to it alone
/// (ActionInstanceComponent, sparse). Every activation path resolves through here.
/// </summary>
/// <remarks>
/// A grant the entity holds itself wins over a definition's grant of the same action, so a
/// per-entity override (a learned scroll, an admin grant) replaces the racial one rather than
/// sitting beside it. Parts applied to it after spawning (AppliedBlueprintComponent) come next, the
/// most recently applied first, then its spawn blueprint's grants -- already merged, one per action, by
/// ResolvedBlueprint. That is composition's own rule, a later part's grant replacing an earlier one's:
/// an applied part is later than everything the entity spawned with. A view over pools with no state of
/// its own -- construct one wherever it is needed.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityActions(
    ActionCatalog actionCatalog,
    BlueprintRegistry? creatures,
    MultiComponentPool<ActionInstanceComponent> instances,
    MultiComponentPool<ActionCooldownComponent> cooldowns,
    DirectComponentPool<SpawnRecordComponent>? spawnRecords = null,
    MultiComponentPool<AppliedBlueprintComponent>? appliedParts = null)
{
    private readonly ActionCatalog _actionCatalog = actionCatalog ?? throw new ArgumentNullException(nameof(actionCatalog));
    /// <inheritdoc cref="EntityBodyParts"/>
    private readonly BlueprintRegistry _creatures = creatures ?? new BlueprintRegistry();
    private readonly MultiComponentPool<ActionInstanceComponent> _instances = instances ?? throw new ArgumentNullException(nameof(instances));
    private readonly MultiComponentPool<ActionCooldownComponent> _cooldowns = cooldowns ?? throw new ArgumentNullException(nameof(cooldowns));

    /// <summary>Builds one from a ComponentManager, for callers that hold the manager rather than the pools (bootstrappers, Presentation).</summary>
    public static EntityActions For(ComponentManager componentManager, ActionCatalog actionCatalog, BlueprintRegistry? creatures)
    {
        ArgumentNullException.ThrowIfNull(componentManager);

        return new EntityActions(
            actionCatalog,
            creatures,
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetMultiPool<ActionCooldownComponent>(),
            componentManager.IsRegistered<SpawnRecordComponent>() ? componentManager.GetDirectPool<SpawnRecordComponent>() : null,
            componentManager.IsRegistered<AppliedBlueprintComponent>() ? componentManager.GetMultiPool<AppliedBlueprintComponent>() : null);
    }

    /// <summary>True when the entity can use actionId at all, whoever granted it.</summary>
    public bool Has(int entityId, Guid actionId) => TryGetGrant(entityId, actionId, out _);

    /// <summary>The definition the entity would actually use for actionId: its own override if it has one, else its race's or class's, else the catalog's.</summary>
    public bool TryGetEffectiveAction(int entityId, Guid actionId, out ActionDefinition definition)
    {
        if (!TryGetGrant(entityId, actionId, out var overrideDefinition))
        {
            definition = null!;
            return false;
        }

        if (overrideDefinition is not null)
        {
            definition = overrideDefinition;
            return true;
        }

        return _actionCatalog.TryGet(actionId, out definition!);
    }

    /// <summary>True while the entity's cooldown on actionId is still running at frame now.</summary>
    public bool IsOnCooldown(int entityId, Guid actionId, long now) =>
        TryGetCooldownDeadline(entityId, actionId, out var readyAtFrame) && !FrameDeadline.IsReached(readyAtFrame, now);

    /// <summary>Frames left on the entity's cooldown on actionId at frame now; 0 when ready.</summary>
    public int CooldownFramesRemaining(int entityId, Guid actionId, long now) =>
        TryGetCooldownDeadline(entityId, actionId, out var readyAtFrame) ? FrameDeadline.Remaining(readyAtFrame, now) : 0;

    /// <summary>Starts the entity's cooldown on actionId: usable again cooldownFrames frames after now.</summary>
    public void SetCooldown(int entityId, Guid actionId, ushort cooldownFrames, long now)
    {
        var readyAtFrame = FrameDeadline.After(now, cooldownFrames);

        if (_cooldowns.TryUpdateFirst(
            entityId,
            (ActionId: actionId, ReadyAt: readyAtFrame),
            static (ref readonly ActionCooldownComponent cooldown, (Guid ActionId, uint ReadyAt) state) => cooldown.ActionId == state.ActionId,
            static (ref ActionCooldownComponent cooldown, (Guid ActionId, uint ReadyAt) state) => cooldown.ReadyAtFrame = state.ReadyAt))
        {
            return;
        }

        _cooldowns.Add(entityId, new ActionCooldownComponent(actionId, readyAtFrame));
    }

    /// <summary>Whether the entity has actionId, and the override that comes with it (null when there is none).</summary>
    private bool TryGetGrant(int entityId, Guid actionId, out ActionDefinition? overrideDefinition)
    {
        if (_instances.TryGetFirst(entityId, actionId, static (ref readonly ActionInstanceComponent candidate, Guid id) => candidate.ActionId == id, out var instance))
        {
            overrideDefinition = instance.Override;
            return true;
        }

        if (TryGetAppliedGrant(entityId, actionId, out overrideDefinition))
        {
            return true;
        }

        if (spawnRecords is not null && spawnRecords.TryGetReadonly(entityId, out var record)
            && _creatures.TryResolve(record.BlueprintId, out var blueprint)
            && blueprint.TryGetAction(actionId, out var grant))
        {
            overrideDefinition = grant.Override;
            return true;
        }

        overrideDefinition = null;
        return false;
    }

    /// <summary>The grant of actionId made by the most recently applied part that makes one.</summary>
    private bool TryGetAppliedGrant(int entityId, Guid actionId, out ActionDefinition? overrideDefinition)
    {
        overrideDefinition = null;
        if (appliedParts is null)
        {
            return false;
        }

        var latestOrder = -1;
        for (var denseIndex = appliedParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = appliedParts.GetNextDenseIndex(denseIndex))
        {
            var applied = appliedParts.GetReadonlyByDenseIndex(denseIndex);
            if (applied.Order <= latestOrder || !_creatures.TryGet(applied.BlueprintId, out var part))
            {
                continue;
            }

            foreach (var grant in part.Actions)
            {
                if (grant.ActionId == actionId)
                {
                    overrideDefinition = grant.Override;
                    latestOrder = applied.Order;
                    break;
                }
            }
        }

        return latestOrder >= 0;
    }

    private bool TryGetCooldownDeadline(int entityId, Guid actionId, out uint readyAtFrame)
    {
        if (_cooldowns.TryGetFirst(entityId, actionId, static (ref readonly ActionCooldownComponent candidate, Guid id) => candidate.ActionId == id, out var cooldown))
        {
            readyAtFrame = cooldown.ReadyAtFrame;
            return true;
        }

        readyAtFrame = 0;
        return false;
    }
}
