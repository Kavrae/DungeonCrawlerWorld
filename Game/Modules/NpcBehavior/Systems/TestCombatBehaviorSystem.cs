using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.Race.Components;
using Game.World;

namespace Game.Modules.NpcBehavior.Systems;

/// <summary>
/// Temporary, deliberately generic priority-chain decision-maker for MovementMode.Random
/// entities: below-half-health-with-a-potion -> self-heal; adjacent to the player or a Fairy ->
/// melee (Punch); otherwise -> wander, the same coin-flip-idle-or-move logic MovementSystem's own
/// Random-mode branch used to own before this system replaced it (see MovementSystem's own doc
/// comment on why it's purely reactive now). Runs before MovementSystem every frame (see
/// GameBootstrapper's module order) so a heal/attack decision this tick actually prevents
/// MovementSystem from also moving the same entity the same frame -- MovementSystem checks for a
/// queued Pending*ActivationComponent before it executes anything.
///
/// Not goblin-specific by name or by filter, despite currently only being exercised by Goblins
/// (the only race with both Punch and, per Goblin's starting-kit change, potions) -- it runs for
/// any Random-mode entity, and each branch is a no-op unless the entity actually carries the
/// components it needs (no Punch AbilityInstanceComponent -> the attack branch never fires; no
/// potion stack -> the heal branch never fires). That's what avoids needing a new system class
/// per NPC race: a future race that wants this exact temporary loadout just needs the same
/// components granted, not a new system.
///
/// One accepted consequence of that generic filter, worth being explicit about: Fairies also
/// carry a Punch AbilityInstanceComponent, so a Fairy adjacent to *another* Fairy will also
/// attack it under this system's plain "player or Fairy" attackable-check -- nothing here
/// excludes "an entity of my own race." This is a real, visible quirk of the generic design, not
/// a bug -- see TODO.md's entry on composing entity behavior from smaller, race-configurable
/// pieces (aggressive/cowardly/prefers-melee/prefers-potions/...), which is where "don't attack
/// my own kind" belongs once it exists, rather than hardcoding it into this temporary stand-in.
///
/// This class is explicitly a stand-in for that future composite-behavior system, not the real
/// thing -- named "Test" deliberately so nothing mistakes it for a permanent design.
/// </summary>
public sealed class TestCombatBehaviorSystem : ISystem
{
    private const byte StripeCountValue = 1;

    public byte StripeCount => StripeCountValue;

    private readonly PackedComponentPool<MovementComponent> _movementPool;
    private readonly DirectComponentPool<TransformComponent> _transformPool;
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks;
    private readonly PackedComponentPool<HealthComponent> _health;
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks;
    private readonly MultiComponentPool<AbilityInstanceComponent> _abilityInstances;
    private readonly MultiComponentPool<RaceComponent> _raceComponents;
    private readonly PackedComponentPool<PendingAbilityActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<PendingConsumableActivationComponent> _pendingConsumableActivations;
    private readonly IMapQuery _mapQuery;
    private readonly MathUtility _mathUtility;
    private readonly IPlayerQuery? _playerQuery;
    private readonly PackedComponentPool<DeadComponent>? _deadEntities;
    private readonly EntityStripeSet _stripeSet;

    private readonly List<Vector3Int> _adjacentTilesBuffer = [];

    public TestCombatBehaviorSystem(
        PackedComponentPool<MovementComponent> movementPool,
        DirectComponentPool<TransformComponent> transformPool,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<HealthComponent> health,
        MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
        MultiComponentPool<AbilityInstanceComponent> abilityInstances,
        MultiComponentPool<RaceComponent> raceComponents,
        PackedComponentPool<PendingAbilityActivationComponent> pendingActivations,
        PackedComponentPool<PendingConsumableActivationComponent> pendingConsumableActivations,
        IMapQuery mapQuery,
        MathUtility mathUtility,
        IPlayerQuery? playerQuery,
        PackedComponentPool<DeadComponent>? deadEntities = null)
    {
        _movementPool = movementPool;
        _transformPool = transformPool;
        _actionLocks = actionLocks;
        _health = health;
        _inventoryStacks = inventoryStacks;
        _abilityInstances = abilityInstances;
        _raceComponents = raceComponents;
        _pendingActivations = pendingActivations;
        _pendingConsumableActivations = pendingConsumableActivations;
        _mapQuery = mapQuery;
        _mathUtility = mathUtility;
        _playerQuery = playerQuery;
        _deadEntities = deadEntities;

        _stripeSet = new EntityStripeSet(StripeCount, movementPool.EntityIds);
        movementPool.EntityAdded += _stripeSet.OnEntityAdded;
        movementPool.EntityRemoved += _stripeSet.OnEntityRemoved;
    }

    public void Update(EngineTime time, byte stripeIndex)
    {
        foreach (var entityId in _stripeSet.GetBucket(stripeIndex))
        {
            if (_deadEntities?.Has(entityId) == true)
            {
                continue;
            }

            ref readonly var movement = ref _movementPool.GetReadonly(entityId);
            if (movement.MovementMode != MovementMode.Random)
            {
                continue;
            }

            if (movement.FramesToWait > 0)
            {
                _movementPool.TryUpdate(entityId, static (ref MovementComponent m) => m.FramesToWait = (short)Math.Max(0, m.FramesToWait - 1));
                continue;
            }

            if (ActionLockGate.IsBlocked(_actionLocks, entityId) || !_transformPool.TryGetReadonly(entityId, out var transform))
            {
                continue;
            }

            if (movement.NextMapPosition is { } pending && pending != transform.Position)
            {
                continue; // Still mid-move from a previous decision -- nothing new to decide yet.
            }

            if (TryDecideSelfHeal(entityId, transform) || TryDecideMeleeAttack(entityId, transform))
            {
                continue;
            }

            DecideWander(entityId, transform);
        }
    }

    /// <summary>Below half health and holding at least one Health Potion -> drink it. Deliberately simple (a fixed 50% threshold, no smarter "how urgent is this" weighing) -- see this class's own doc comment on why.</summary>
    private bool TryDecideSelfHeal(int entityId, TransformComponent transform)
    {
        if (!_health.TryGetReadonly(entityId, out var health) || health.CurrentHealth * 2 >= health.MaximumHealth)
        {
            return false;
        }

        if (!InventoryQueries.TryGetStack(_inventoryStacks, entityId, CoreItemsModule.HealthPotionId, out var potionStack) || potionStack.Quantity <= 0)
        {
            return false;
        }

        _pendingConsumableActivations.Merge(entityId, new PendingConsumableActivationComponent(CoreItemsModule.HealthPotionId, [transform.Position]));
        return true;
    }

    /// <summary>
    /// Only fires if this entity was actually granted Punch. Queues the whole resolved Adjacent
    /// footprint (now excluding the entity's own tiles, see TargetShapeResolver) rather than a
    /// single target tile -- AbilityEffectResolver figures out who's actually there, the same
    /// "let the resolver sort it out" pattern ActionTargetingController.TryActivateWithAutoTarget
    /// already uses for player-driven Adjacent abilities.
    /// </summary>
    private bool TryDecideMeleeAttack(int entityId, TransformComponent transform)
    {
        if (!AbilityInstanceQueries.TryGet(_abilityInstances, entityId, CoreAbilitiesModule.PunchId, out _))
        {
            return false;
        }

        TargetShapeResolver.Resolve(TargetShape.Adjacent, transform.Position, transform.Size, transform.Position, range: 0, areaSize: 0, _mapQuery.MapSize, _adjacentTilesBuffer);

        if (!HasAttackableNeighbor(_adjacentTilesBuffer))
        {
            return false;
        }

        _pendingActivations.Merge(entityId, new PendingAbilityActivationComponent(CoreAbilitiesModule.PunchId, _adjacentTilesBuffer.ToArray()));
        return true;
    }

    /// <summary>
    /// Checks both the Blocking occupant and every non-Blocking occupant of each tile (see
    /// AbilityEffectResolver.Apply's own dual loop) -- melee is not restricted to Blocking
    /// targets only, so a non-Blocking Fairy/player sharing an adjacent tile still counts.
    /// </summary>
    private bool HasAttackableNeighbor(List<Vector3Int> adjacentTiles)
    {
        foreach (var tile in adjacentTiles)
        {
            var blockingEntityId = _mapQuery.GetEntityIdAt(tile);
            if (blockingEntityId != -1 && IsAttackable(blockingEntityId))
            {
                return true;
            }

            foreach (var nonBlockingEntityId in _mapQuery.GetNonBlockingEntityIdsAt(tile))
            {
                if (IsAttackable(nonBlockingEntityId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsAttackable(int candidateEntityId) =>
        candidateEntityId == _playerQuery?.PlayerEntityId || IsFairy(candidateEntityId);

    private bool IsFairy(int entityId)
    {
        for (var denseIndex = _raceComponents.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = _raceComponents.GetNextDenseIndex(denseIndex))
        {
            if (_raceComponents.GetReadonlyByDenseIndex(denseIndex).Id == Fairy.RaceId)
            {
                return true;
            }
        }

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
