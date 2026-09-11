using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race.Components;
using Game.World;

namespace Game.Modules.NpcBehavior.Systems;

/// <summary>
/// Temporary, deliberately generic priority-chain decision-maker for MovementMode.Random
/// entities: below-half-health-with-a-potion -> self-heal; adjacent to an entity of a different
/// race -> melee (randomly QuickAttack or PowerAttack, see TryDecideMeleeAttack); otherwise ->
/// wander, the same coin-flip-idle-or-move logic MovementSystem's own Random-mode branch used to
/// own before this system replaced it (see MovementSystem's own doc comment on why it's purely
/// reactive now). Runs before MovementSystem every frame (see GameBootstrapper's module order) so
/// a heal/attack decision this tick actually prevents MovementSystem from also moving the same
/// entity the same frame -- MovementSystem checks for a queued Pending*ActivationComponent before
/// it executes anything.
///
/// Not goblin-specific by name or by filter, despite currently only being exercised by Goblins
/// (the only race with both QuickAttack/PowerAttack and, per Goblin's starting-kit change,
/// potions) -- it runs for any Random-mode entity, and each branch is a no-op unless the entity
/// actually carries the components it needs (no QuickAttack ActionInstanceComponent -> the attack
/// branch never fires; no potion stack -> the heal branch never fires). That's what avoids needing
/// a new system class per NPC race: a future race that wants this exact temporary loadout just
/// needs the same components granted, not a new system.
///
/// "Different race" (IsAttackable) is a real RaceComponent comparison, not a name/id allowlist --
/// an entity with no RaceComponent at all is never attackable (nothing to compare), and two
/// entities sharing the same race never attack each other (a Fairy adjacent to another Fairy no
/// longer does, unlike this system's earlier player-or-Fairy-only check). The player counts as
/// "a different race" the ordinary way, by actually being Human (PLAN-human-race.md) -- no
/// explicit player special-case needed. See TODO.md's entry on composing entity behavior from
/// smaller, race-configurable pieces (aggressive/cowardly/prefers-melee/prefers-potions/...) for
/// where finer-grained targeting (e.g. faction alliances that aren't just "same race or not")
/// belongs once it exists, rather than hardcoding it into this temporary stand-in.
///
/// This class is explicitly a stand-in for that future composite-behavior system, not the real
/// thing -- named "Test" deliberately so nothing mistakes it for a permanent design.
/// </summary>
public sealed class TestCombatBehaviorSystem : ITieredSystem
{
    // Matches MovementSystem's StripeCount, and (since this system became tiered too) its tier
    // divisors: both are wired to the same MovementComponent pool via ProcessingTierWiring and
    // both derive "due" from EngineTime.FrameCount, so an entity lands in the same bucket of the
    // same tier in both and is still decided and moved on the same frame. Previously 1, meaning
    // this system processed its entire population every frame while MovementSystem only processed
    // 1/15th -- the actual cause of the ~2fps slowdown investigated via TODO.md's "Very slow"
    // note.
    private const byte StripeCountValue = 15;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<MovementComponent> _movementPool;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<SimpleHealthComponent> _health;
    private readonly MultiComponentPool<BodyPartComponent> _bodyParts;
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks;
    private readonly MultiComponentPool<ActionInstanceComponent> _actionInstances;
    private readonly MultiComponentPool<RaceComponent> _raceComponents;
    private readonly PackedComponentPool<PendingActionActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<PendingConsumableActivationComponent> _pendingConsumableActivations;
    private readonly IMapQuery _mapQuery;
    private readonly MathUtility _mathUtility;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly TieredEntityStripeSet _tieredStripeSet;

    private readonly List<Vector3Int> _adjacentTilesBuffer = [];

    public TestCombatBehaviorSystem(
        PackedComponentPool<MovementComponent> movementPool,
        DirectComponentPool<TransformComponent> transformPool,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<SimpleHealthComponent> health,
        MultiComponentPool<BodyPartComponent> bodyParts,
        MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
        MultiComponentPool<ActionInstanceComponent> actionInstances,
        MultiComponentPool<RaceComponent> raceComponents,
        PackedComponentPool<PendingActionActivationComponent> pendingActivations,
        PackedComponentPool<PendingConsumableActivationComponent> pendingConsumableActivations,
        IMapQuery mapQuery,
        MathUtility mathUtility,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent>? deadEntities = null)
    {
        _movementPool = movementPool;
        _transformPool = transformPool;
        _actionLocks = actionLocks;
        _health = health;
        _bodyParts = bodyParts;
        _inventoryStacks = inventoryStacks;
        _actionInstances = actionInstances;
        _raceComponents = raceComponents;
        _pendingActivations = pendingActivations;
        _pendingConsumableActivations = pendingConsumableActivations;
        _mapQuery = mapQuery;
        _mathUtility = mathUtility;
        _deadEntities = deadEntities;

        _tieredStripeSet = ProcessingTierWiring.CreateAndWire(StripeCount, movementPool, processingTiers, processingTierEvents);
    }

    /// <summary>
    /// Driven per processing tier by TieredSystemRunner, so a distant entity's decision-making runs at its
    /// tier's cadence rather than at the flat base one every entity used to share. This was the
    /// single largest per-frame cost in the game (measured at 146ms/sec of a 438ms/sec simulation
    /// budget), and the overwhelming majority of it was spent deciding, for entities nowhere near
    /// the player, that they had nothing to do -- the priority chain below reads seven-plus
    /// entity-indexed pools before most entities reach a no-op branch.
    ///
    /// Keyed off FrameCount rather than the stripeIndex parameter, matching MovementSystem: both
    /// are wired to the same MovementComponent pool with the same base StripeCount and the same
    /// tier divisors, so they still bucket identically and an entity is still decided and moved
    /// on the same frame -- which is what makes this system's ordering ahead of MovementSystem
    /// (see NpcBehaviorModule) meaningful. stripeIndex is accepted for ISystem compliance and
    /// otherwise unused, the same way StatusEffectAuraSystem already treats it.
    /// </summary>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        foreach (var entityId in entityIds)
        {
            DecideForEntity(entityId);
        }
    }

    /// <summary>One due entity's decision step. Takes no frames-per-visit: this system owns no countdown -- FramesToWait belongs to MovementSystem and is only read here, as a gate.</summary>
    private void DecideForEntity(int entityId)
    {
        if (_deadEntities?.Has(entityId) == true)
        {
            return;
        }

        if (!_transformPool.TryGetReadonly(entityId, out var transform))
        {
            return;
        }

        ref readonly var movement = ref _movementPool.GetReadonly(entityId);
        if (movement.MovementMode != MovementMode.Random)
        {
            return;
        }

        // A pure gate: MovementSystem owns the FramesToWait countdown, this system only refuses
        // to decide while it is running. Both used to decrement it, and because the two are wired
        // to the same pool with the same stripe count and divisors -- and SystemManager derives
        // stripeIndex as FrameCount % StripeCount -- they visited the same entity on the same
        // frame and each took a full span off, so every wait elapsed at twice its intended rate.
        // MovementSystem is the right owner despite being "purely reactive" about destinations:
        // FramesToWait gates movement execution, and a single owner is what stops the two from
        // drifting again.
        if (movement.FramesToWait > 0)
        {
            return;
        }

        if (ActionLockGate.IsBlocked(_actionLocks, entityId) )
        {
            return;
        }

        if (movement.NextMapPosition is { } pending && pending != transform.Position)
        {
            return; // Still mid-move from a previous decision -- nothing new to decide yet.
        }

        if (TryDecideSelfHeal(entityId, transform) || TryDecideMeleeAttack(entityId, transform))
        {
            return;
        }

        DecideWander(entityId, transform);
    }

    /// <summary>Below half health and holding at least one Health Potion -> drink it. Deliberately simple (a fixed 50% threshold, no smarter "how urgent is this" weighing) -- see this class's own doc comment on why.</summary>
    private bool TryDecideSelfHeal(int entityId, TransformComponent transform)
    {
        if (!HealthQueries.TryGetTotals(_health, _bodyParts, entityId, out var current, out var maximum) || current * 2 >= maximum)
        {
            return false;
        }

        if (!InventoryQueries.TryGetStack(_inventoryStacks, entityId, HealthPotion.Id, out var potionStack) || potionStack.Quantity <= 0)
        {
            return false;
        }

        _pendingConsumableActivations.Merge(entityId, new PendingConsumableActivationComponent(potionStack.StackInstanceId, [transform.Position]));
        return true;
    }

    /// <summary>
    /// Only fires if this entity was actually granted QuickAttack (every race grants QuickAttack
    /// and PowerAttack as a pair -- see the race blueprints -- so QuickAttack's presence gates the
    /// whole branch). Randomly picks QuickAttack or PowerAttack per attack so NPCs actually
    /// exercise PowerAttack's telegraph/Dodge interaction too, not just the player (Combat
    /// Overhaul: Dodge, TODO.md). Queues the whole resolved Adjacent footprint (now excluding the
    /// entity's own tiles, see TargetShapeResolver) rather than a single target tile --
    /// ActionEffectResolver figures out who's actually there, the same "let the resolver sort it
    /// out" pattern ActionTargetingController.TryActivateWithAutoTarget already uses for
    /// player-driven Adjacent actions.
    /// </summary>
    private bool TryDecideMeleeAttack(int entityId, TransformComponent transform)
    {
        if (!ActionInstanceQueries.TryGet(_actionInstances, entityId, QuickAttackAction.Id, out _))
        {
            return false;
        }

        // No race, no notion of "a different race" to attack -- bail before even resolving the
        // footprint. Every real race blueprint grants a RaceComponent, so this only ever fires
        // defensively (this system explicitly isn't goblin-specific, see its own doc comment).
        if (!TryGetRaceId(entityId, out var attackerRaceId))
        {
            return false;
        }

        TargetShapeResolver.Resolve(TargetShape.Adjacent, transform.Position, transform.Size, transform.Position, range: 0, areaSize: 0, _mapQuery.MapSize, _adjacentTilesBuffer);

        if (!HasAttackableNeighbor(_adjacentTilesBuffer, attackerRaceId))
        {
            return false;
        }

        var actionId = _mathUtility.Next(0, 2) == 0 ? QuickAttackAction.Id : PowerAttackAction.Id;
        _pendingActivations.Merge(entityId, new PendingActionActivationComponent(actionId, _adjacentTilesBuffer.ToArray()));
        return true;
    }

    /// <summary>
    /// Checks every occupant of each tile, Blocking or not -- melee is not restricted to
    /// Blocking targets only, so a non-Blocking Fairy/player sharing an adjacent tile still
    /// counts.
    /// </summary>
    private bool HasAttackableNeighbor(List<Vector3Int> adjacentTiles, Guid attackerRaceId)
    {
        foreach (var tile in adjacentTiles)
        {
            foreach (var occupantEntityId in _mapQuery.GetOccupantEntityIdsAt(tile))
            {
                if (IsAttackable(occupantEntityId, attackerRaceId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Excludes a dead candidate first -- a corpse stays fully populated and occupying its tile
    /// for future looting (DeathSystem never calls EntityManager.DestroyEntity, see this repo's
    /// own IMPLEMENTATION-NOTES.md). Then requires the candidate to actually carry a RaceComponent
    /// of a race different from the attacker's own -- a raceless entity (a shop, a container, any
    /// non-creature prop) is never attackable, and two entities of the same race never attack each
    /// other (see this class's own doc comment on why that's now a real comparison, not a
    /// player-or-Fairy allowlist).
    /// </summary>
    private bool IsAttackable(int candidateEntityId, Guid attackerRaceId) =>
        _deadEntities?.Has(candidateEntityId) != true &&
        TryGetRaceId(candidateEntityId, out var candidateRaceId) &&
        candidateRaceId != attackerRaceId;

    /// <summary>First RaceComponent found for entityId (a real entity carries exactly one), or false if it has none.</summary>
    private bool TryGetRaceId(int entityId, out Guid raceId)
    {
        for (var denseIndex = _raceComponents.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _raceComponents.GetNextDenseIndex(denseIndex))
        {
            raceId = _raceComponents.GetReadonlyByDenseIndex(denseIndex).Id;
            return true;
        }

        raceId = default;
        return false;
    }

    /// <summary>The exact coin-flip-idle-or-move logic MovementSystem's own Random-mode branch used to run directly -- moved here unchanged, now writing NextMapPosition/FramesToWait as this system's own decision rather than MovementSystem deciding and executing in the same call.</summary>
    private void DecideWander(int entityId, TransformComponent transform)
    {
        if (_mathUtility.Next(0, 2) == 0)
        {
            SetIdle(entityId);
            return;
        }

        var isBlocking = _mapQuery.IsBlocking(entityId);
        if (MovementCandidates.TryPickRandomAdjacentPosition(_mapQuery, _mathUtility, entityId, transform.Position, transform.Size, isBlocking, out var candidatePosition))
        {
            _movementPool.TryUpdate(entityId, candidatePosition, static (ref MovementComponent m, Vector3Int candidate) => m.NextMapPosition = candidate);
            return;
        }

        SetIdle(entityId);
    }

    private void SetIdle(int entityId) =>
        _movementPool.TryUpdate(entityId, static (ref MovementComponent m) => m.FramesToWait = MovementCandidates.FramesToWaitIfNoOptions);
}
