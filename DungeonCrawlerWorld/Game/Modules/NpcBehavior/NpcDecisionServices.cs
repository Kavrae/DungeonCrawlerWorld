using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Engine.Tags;
using Game.Blueprints;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement.Components;
using Game.Modules.NpcBehavior.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race.Components;
using Game.Spawning;
using Game.World;

namespace Game.Modules.NpcBehavior;

/// <summary>What every NPC decision reads and writes through, built once with the decision system: the pools and queries, the intents, the decision's random sequence and the scratch buffers.</summary>
/// <remarks>
/// One per decision system -- the scratch and the sequence are reused decision after decision -- and never shared between
/// threads. NpcDecisionContext is a ref struct over it, holding only what is particular to one decision.
/// </remarks>
public sealed class NpcDecisionServices
{
    private readonly BlueprintRegistry _definitions;
    private readonly ActionCatalog _actionCatalog;
    private readonly MultiComponentPool<ActionInstanceComponent> _actionInstances;
    private readonly MultiComponentPool<AppliedBlueprintComponent> _appliedParts;
    private ActionDefinition[]?[] _blueprintActions = [];

    public NpcDecisionServices(GameModuleContext context, ComponentManager componentManager, ulong behaviorStreamSeed)
    {
        _definitions = context.Definitions;
        _actionCatalog = context.Actions;
        _actionInstances = componentManager.GetMultiPool<ActionInstanceComponent>();
        _appliedParts = componentManager.GetMultiPool<AppliedBlueprintComponent>();
        BehaviorStreamSeed = behaviorStreamSeed;
        Rolls = new MathUtility(Random);

        Transforms = componentManager.GetDirectPool<TransformComponent>();
        SpawnRecords = componentManager.GetDirectPool<SpawnRecordComponent>();
        Movements = componentManager.GetPackedPool<MovementComponent>();
        Decisions = componentManager.GetPackedPool<NpcDecisionComponent>();
        ActionLocks = componentManager.GetPackedPool<ActionLockComponent>();
        Health = componentManager.GetPackedPool<SimpleHealthComponent>();
        BodyParts = EntityBodyParts.For(componentManager, context.Definitions);
        Stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        RaceSlots = componentManager.GetPackedPool<RaceSlotsComponent>();
        Dead = componentManager.GetPackedPool<DeadComponent>();
        MeleeDisabled = componentManager.GetPackedPool<MeleeDisabledComponent>();
        Tiers = new ProcessingTierQuery(componentManager.GetDirectPool<ProcessingTierComponent>());
        Items = context.Items;
        Actions = EntityActions.For(componentManager, context.Actions, context.Definitions);
        Map = context.MapQuery;
        SightBlockers = context.SightBlockers;
        Player = context.PlayerQuery;
        Targeting = context.TargetResolution;
        Effects = context.EffectServices;
        EntityKeys = context.EntityKeys;
        Intents = IntentWriter.For(componentManager);
        Looting = new NpcLooting(componentManager, context.Items, context.PlayerQuery, context.EntityKeys, context.MapQuery, Tiers);
    }

    public DirectComponentPool<TransformComponent> Transforms { get; }

    public DirectComponentPool<SpawnRecordComponent> SpawnRecords { get; }

    public PackedComponentPool<MovementComponent> Movements { get; }

    public PackedComponentPool<NpcDecisionComponent> Decisions { get; }

    public PackedComponentPool<ActionLockComponent> ActionLocks { get; }

    public PackedComponentPool<SimpleHealthComponent> Health { get; }

    public EntityBodyParts BodyParts { get; }

    public MultiComponentPool<InventoryItemStackComponent> Stacks { get; }

    public PackedComponentPool<RaceSlotsComponent> RaceSlots { get; }

    public PackedComponentPool<DeadComponent> Dead { get; }

    public PackedComponentPool<MeleeDisabledComponent> MeleeDisabled { get; }

    public ProcessingTierQuery Tiers { get; }

    public ItemCatalog Items { get; }

    public EntityActions Actions { get; }

    public IMapQuery Map { get; }

    public Engine.Tiles.ISightBlockers SightBlockers { get; }

    public IPlayerQuery Player { get; }

    public TargetResolution Targeting { get; }

    public EffectServices Effects { get; }

    public EntityKeys EntityKeys { get; }

    public IntentWriter Intents { get; }

    public NpcLooting Looting { get; }

    /// <summary>The behavior stream's seed (NpcBehaviorModule.BehaviorRandomStream), which each decision's sequence is derived from.</summary>
    public ulong BehaviorStreamSeed { get; }

    /// <summary>The one sequence every decision rolls from, reseeded for each (NpcDecisionContext).</summary>
    public SeededRandom Random { get; } = new();

    /// <summary>Random, as what a behavior's Act rolls with.</summary>
    public MathUtility Rolls { get; }

    /// <summary>Scratch for the actions a behavior picks among.</summary>
    public List<ActionDefinition> CandidateActions { get; } = [];

    /// <summary>Scratch for an entity's action ids, when it holds actions of its own.</summary>
    public List<Guid> ActionIds { get; } = [];

    /// <summary>Scratch for an entity's adjacent tiles.</summary>
    public List<Vector3Int> AdjacentTiles { get; } = [];

    /// <summary>Scratch for the entities a perception scan has counted, so a creature larger than a tile counts once.</summary>
    public HashSet<int> CountedEntityIds { get; } = [];

    /// <summary>Fills destination with every action entityId can use that carries firstRequiredTag and, unless it is None, secondRequiredTag (parent-aware), and isn't a toggle, in the order it holds them; destination is cleared first.</summary>
    /// <remarks>
    /// An entity holding no action of its own and no applied part -- nearly every creature -- reads its blueprint's
    /// actions, resolved once per blueprint, so asking costs a scan of a few tag sets. Any other reads each action it
    /// holds through EntityActions.
    /// </remarks>
    public void CollectActions(int entityId, GameplayTag firstRequiredTag, GameplayTag secondRequiredTag, List<ActionDefinition> destination)
    {
        destination.Clear();
        if (!_actionInstances.Has(entityId) && !_appliedParts.Has(entityId) && SpawnRecords.TryGetReadonly(entityId, out var record))
        {
            foreach (var action in BlueprintActions(record.BlueprintId))
            {
                AddIfTagged(action, firstRequiredTag, secondRequiredTag, destination);
            }

            return;
        }

        Actions.CopyActionIds(entityId, ActionIds);
        foreach (var actionId in ActionIds)
        {
            if (Actions.TryGetEffectiveAction(entityId, actionId, out var action))
            {
                AddIfTagged(action, firstRequiredTag, secondRequiredTag, destination);
            }
        }
    }

    /// <summary>Whether entityId could use action now: nothing blocks it and it is ready (its cooldown run out; the lock is the decision's own gate).</summary>
    public bool IsUsable(int entityId, ActionDefinition action, long now) =>
        ActivationQueries.GetBlocker(entityId, action, action.Activator, isToggledOn: false, MeleeDisabled, Effects, now) == ActivationBlocker.None &&
        ActivationQueries.FramesUntilReady(entityId, action, Actions, ActionLocks, now) == 0;

    /// <summary>Removes from actions every one entityId can't use now (IsUsable), keeping the rest in order.</summary>
    public void KeepUsable(int entityId, List<ActionDefinition> actions, long now)
    {
        var kept = 0;
        for (var index = 0; index < actions.Count; index++)
        {
            if (IsUsable(entityId, actions[index], now))
            {
                actions[kept++] = actions[index];
            }
        }

        actions.RemoveRange(kept, actions.Count - kept);
    }

    /// <summary>Whether entityId could use item, the effective item of its stack, now: as IsUsable for an action, with the stack's own cooldown.</summary>
    public bool IsUsable(int entityId, in InventoryItemStackComponent stack, ItemDefinition item, long now) =>
        item.Activator is { } activator &&
        ActivationQueries.GetBlocker(entityId, item, activator, isToggledOn: false, MeleeDisabled, Effects, now) == ActivationBlocker.None &&
        InventoryQueries.FramesUntilReady(entityId, in stack, item, ActionLocks, now) == 0;

    /// <summary>Whether entityId can be fought by something of raceId: simulated, not dead, and of a different primary race.</summary>
    public bool IsHostileTo(int entityId, ushort raceId) =>
        Tiers.IsSimulated(entityId) &&
        !Dead.Has(entityId) &&
        TryGetRaceId(entityId, out var otherRaceId) &&
        otherRaceId != raceId;

    /// <summary>Whether entityId stands with something of raceId: simulated, not dead, and of the same primary race.</summary>
    public bool IsAllyOf(int entityId, ushort raceId) =>
        Tiers.IsSimulated(entityId) &&
        !Dead.Has(entityId) &&
        TryGetRaceId(entityId, out var otherRaceId) &&
        otherRaceId == raceId;

    /// <summary>The race entityId counts as (its first slot), or false when it has none.</summary>
    public bool TryGetRaceId(int entityId, out ushort raceId)
    {
        raceId = RaceSlots.TryGetReadonly(entityId, out var slots) ? slots.Primary : RaceSlotsComponent.Empty;
        return raceId != RaceSlotsComponent.Empty;
    }

    private ActionDefinition[] BlueprintActions(ushort blueprintId)
    {
        if (blueprintId < _blueprintActions.Length && _blueprintActions[blueprintId] is { } cached)
        {
            return cached;
        }

        var resolved = _definitions.Resolve(blueprintId);
        var actions = new List<ActionDefinition>(resolved.Actions.Count);
        foreach (var grant in resolved.Actions)
        {
            if ((grant.ResolveOverride(_actionCatalog) ?? (_actionCatalog.TryGet(grant.ActionId, out var definition) ? definition : null)) is { } action)
            {
                actions.Add(action);
            }
        }

        if (blueprintId >= _blueprintActions.Length)
        {
            Array.Resize(ref _blueprintActions, System.Math.Max(blueprintId + 1, _definitions.Count + 1));
        }

        return _blueprintActions[blueprintId] = [.. actions];
    }

    private static void AddIfTagged(ActionDefinition action, GameplayTag firstRequiredTag, GameplayTag secondRequiredTag, List<ActionDefinition> destination)
    {
        if (action.Toggle is null && action.Tags.Has(firstRequiredTag) && (secondRequiredTag.IsNone || action.Tags.Has(secondRequiredTag)))
        {
            destination.Add(action);
        }
    }
}
