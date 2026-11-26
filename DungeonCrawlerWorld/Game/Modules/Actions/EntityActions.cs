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
/// an applied part is later than everything the entity spawned with. A crawler (SpawnFlags.Crawler) also has
/// every Core action (ActionCatalog.CoreActionIds), found last, so any grant of one wins over the catalog's
/// definition. A view over pools with no state of
/// its own -- construct one wherever it is needed.
///
/// Every Guid is taken as the action it resolves to (ActionCatalog.ResolveId): a disabled action's grant is a grant of
/// its replacement, so an entity granted both is granted one action, listed once, with one cooldown, and a hotkey bound
/// to the old Guid uses the replacement. Among grants that resolve to one action, one naming it directly wins.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EntityActions(
    ActionCatalog actionCatalog,
    BlueprintRegistry creatures,
    MultiComponentPool<ActionInstanceComponent> instances,
    MultiComponentPool<ActionCooldownComponent> cooldowns,
    DirectComponentPool<SpawnRecordComponent> spawnRecords,
    MultiComponentPool<AppliedBlueprintComponent> appliedParts)
{
    /// <summary>Builds one from a ComponentManager, for callers that hold the manager rather than the pools (bootstrappers, Presentation).</summary>
    public static EntityActions For(ComponentManager componentManager, ActionCatalog actionCatalog, BlueprintRegistry creatures)
    {
        ArgumentNullException.ThrowIfNull(componentManager);

        return new EntityActions(
            actionCatalog,
            creatures,
            componentManager.GetMultiPool<ActionInstanceComponent>(),
            componentManager.GetMultiPool<ActionCooldownComponent>(),
            componentManager.GetDirectPool<SpawnRecordComponent>(),
            componentManager.GetMultiPool<AppliedBlueprintComponent>());
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

        return actionCatalog.TryGet(actionId, out definition!);
    }

    /// <summary>Fills destination with every action entityId can use, each once: its own grants, its applied parts', its spawn blueprint's, then for a crawler the Core actions.</summary>
    /// <remarks>Ids only; TryGetEffectiveAction gives the definition each resolves to. destination is cleared first.</remarks>
    public void CopyActionIds(int entityId, List<Guid> destination)
    {
        destination.Clear();
        for (var denseIndex = instances.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = instances.GetNextDenseIndex(denseIndex))
        {
            AddOnce(destination, Resolve(instances.GetReadonlyByDenseIndex(denseIndex).ActionId));
        }

        for (var denseIndex = appliedParts.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = appliedParts.GetNextDenseIndex(denseIndex))
        {
            if (creatures.TryGet(appliedParts.GetReadonlyByDenseIndex(denseIndex).BlueprintId, out var part))
            {
                foreach (var grant in part.Actions)
                {
                    AddOnce(destination, Resolve(grant.ActionId));
                }
            }
        }

        if (!spawnRecords.TryGetReadonly(entityId, out var record))
        {
            return;
        }

        if (creatures.TryResolve(record.BlueprintId, out var blueprint))
        {
            foreach (var grant in blueprint.Actions)
            {
                AddOnce(destination, Resolve(grant.ActionId));
            }
        }

        if (IsCrawler(record))
        {
            foreach (var coreActionId in actionCatalog.CoreActionIds)
            {
                AddOnce(destination, Resolve(coreActionId));
            }
        }
    }

    /// <summary>The action actionId stands for: itself, or the end of a disabled action's replacements.</summary>
    private Guid Resolve(Guid actionId) => actionCatalog.ResolveId(actionId);

    /// <summary>Whether actionId is a Core action, which a crawler has whatever it was built from.</summary>
    public bool IsCore(Guid actionId) => actionCatalog.IsCore(actionId);

    /// <summary>A number that changes whenever what the entity itself was granted or applied changes -- what CopyActionIds reads beyond its fixed spawn blueprint.</summary>
    public uint GetActionSetVersion(int entityId) => unchecked(instances.GetEntityVersion(entityId) + appliedParts.GetEntityVersion(entityId));

    private static void AddOnce(List<Guid> destination, Guid actionId)
    {
        if (!destination.Contains(actionId))
        {
            destination.Add(actionId);
        }
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
        actionId = Resolve(actionId);
        var readyAtFrame = FrameDeadline.After(now, cooldownFrames);

        using (var heldCooldown = cooldowns.TryWriteFirst(entityId, new CooldownMatcher(actionId)))
        {
            if (heldCooldown.Found)
            {
                heldCooldown.Value.ReadyAtFrame = readyAtFrame;
                return;
            }
        }

        cooldowns.Add(entityId, new ActionCooldownComponent(actionId, readyAtFrame));
    }

    /// <summary>Whether the entity has actionId, and the override that comes with it (null when there is none).</summary>
    private bool TryGetGrant(int entityId, Guid actionId, out ActionDefinition? overrideDefinition)
    {
        actionId = Resolve(actionId);
        if (TryGetOwnGrant(entityId, actionId, out overrideDefinition))
        {
            return true;
        }

        if (TryGetAppliedGrant(entityId, actionId, out overrideDefinition))
        {
            return true;
        }

        overrideDefinition = null;
        if (!spawnRecords.TryGetReadonly(entityId, out var record))
        {
            return false;
        }

        if (creatures.TryResolve(record.BlueprintId, out var blueprint) && TryFindGrant(blueprint.Actions, actionId, out var grant))
        {
            overrideDefinition = grant.ResolveOverride(actionCatalog);
            return true;
        }

        return IsCrawler(record) && IsResolvedCore(actionId);
    }

    /// <summary>Whether the resolved actionId is a Core action or what one resolves to.</summary>
    private bool IsResolvedCore(Guid actionId)
    {
        if (actionCatalog.IsCore(actionId))
        {
            return true;
        }

        if (!actionCatalog.HasRedirects)
        {
            return false;
        }

        foreach (var coreActionId in actionCatalog.CoreActionIds)
        {
            if (Resolve(coreActionId) == actionId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The entity's own grant of the resolved actionId: one naming it, else one naming an action that resolves to it.</summary>
    private bool TryGetOwnGrant(int entityId, Guid actionId, out ActionDefinition? overrideDefinition)
    {
        if (instances.TryGetFirst(entityId, new InstanceMatcher(actionId), out var instance))
        {
            overrideDefinition = instance.Override;
            return true;
        }

        if (!actionCatalog.HasRedirects)
        {
            overrideDefinition = null;
            return false;
        }

        for (var denseIndex = instances.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = instances.GetNextDenseIndex(denseIndex))
        {
            var held = instances.GetReadonlyByDenseIndex(denseIndex);
            if (Resolve(held.ActionId) == actionId)
            {
                overrideDefinition = held.Override;
                return true;
            }
        }

        overrideDefinition = null;
        return false;
    }

    /// <summary>The grant among grants of the resolved actionId: one naming it, else the first naming an action that resolves to it.</summary>
    private bool TryFindGrant(IReadOnlyList<ActionGrant> grants, Guid actionId, out ActionGrant grant)
    {
        ActionGrant? redirected = null;
        for (var index = 0; index < grants.Count; index++)
        {
            if (grants[index].ActionId == actionId)
            {
                grant = grants[index];
                return true;
            }

            if (redirected is null && Resolve(grants[index].ActionId) == actionId)
            {
                redirected = grants[index];
            }
        }

        grant = redirected!;
        return redirected is not null;
    }

    /// <summary>Whether the entity was spawned as a crawler and still is one: the flag is cleared from a crawler built after the session's crawler numbers ran out.</summary>
    private static bool IsCrawler(in SpawnRecordComponent record) => record.Flags.HasFlag(SpawnFlags.Crawler);

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
            if (applied.Order <= latestOrder || !creatures.TryGet(applied.BlueprintId, out var part))
            {
                continue;
            }

            if (TryFindGrant(part.Actions, actionId, out var grant))
            {
                overrideDefinition = grant.ResolveOverride(actionCatalog);
                latestOrder = applied.Order;
            }
        }

        return latestOrder >= 0;
    }

    private bool TryGetCooldownDeadline(int entityId, Guid actionId, out uint readyAtFrame)
    {
        if (cooldowns.TryGetFirst(entityId, new CooldownMatcher(Resolve(actionId)), out var cooldown))
        {
            readyAtFrame = cooldown.ReadyAtFrame;
            return true;
        }

        readyAtFrame = 0;
        return false;
    }

    private readonly struct InstanceMatcher(Guid actionId) : IComponentMatcher<ActionInstanceComponent>
    {
        public bool Matches(in ActionInstanceComponent instance) => instance.ActionId == actionId;
    }

    private readonly struct CooldownMatcher(Guid actionId) : IComponentMatcher<ActionCooldownComponent>
    {
        public bool Matches(in ActionCooldownComponent cooldown) => cooldown.ActionId == actionId;
    }
}
