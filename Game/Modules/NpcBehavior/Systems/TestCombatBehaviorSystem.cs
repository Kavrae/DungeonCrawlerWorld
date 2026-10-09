using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Actions.Definitions.Spells;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Mana.Components;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.NpcBehavior.Systems;

/// <summary>
/// TemporaryForFloor, deliberately generic priority-chain decision-maker for MovementMode.Random
/// entities: below-half-health-with-a-potion -> self-heal; adjacent to an entity of a different
/// race -> melee (randomly QuickAttack or PowerAttack, see TryDecideMeleeAttack); otherwise ->
/// wander, the same coin-flip-idle-or-move logic MovementSystem's own Random-mode branch used to
/// own before this system replaced it (see MovementSystem's own doc comment on why it's purely
/// reactive now). Queuing a heal or an attack clears the entity's step (NextMapPosition), and this
/// runs before MovementSystem every frame (NpcBehaviorModule runs before MovementModule), so the
/// entity doesn't also move that frame -- the same rule PlayerCommands follows for the player.
///
/// Not goblin-specific by name or by filter, despite currently only being exercised by Goblins
/// (the only race with both QuickAttack/PowerAttack and, per Goblin's starting-kit change,
/// potions) -- it runs for any Random-mode entity, and each branch is a no-op unless the entity
/// actually carries the components it needs (no QuickAttack ActionInstanceComponent -> the attack
/// branch never fires; no potion stack -> the heal branch never fires). That's what avoids needing
/// a new system class per NPC race: a future race that wants this exact temporary loadout just
/// needs the same components granted, not a new system.
///
/// "Different race" (IsAttackable) is a real race comparison, not a name/id allowlist --
/// an entity with no race at all is never attackable (nothing to compare), and two
/// entities sharing the same race never attack each other (a Fairy adjacent to another Fairy no
/// longer does, unlike this system's earlier player-or-Fairy-only check). The player counts as
/// "a different race" the ordinary way, by actually being Human -- no
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
    private readonly EntityBodyParts _bodyParts;
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks;
    private readonly EntityActions _actions;
    private readonly PackedComponentPool<RaceSlotsComponent> _raceSlots;
    private readonly PackedComponentPool<PendingActionActivationComponent> _pendingActivations;
    private readonly PackedComponentPool<PendingItemActivationComponent> _pendingItemActivations;
    private readonly IMapQuery _mapQuery;
    private readonly MathUtility _mathUtility;
    private readonly PackedComponentPool<DeadComponent> _deadEntities;
    private readonly EffectServices _effectServices;
    private readonly PackedComponentPool<ManaComponent> _mana;
    private readonly MultiComponentPool<StatModifierComponent> _statModifiers;
    private readonly PackedComponentPool<MeleeDisabledComponent> _meleeDisabled;
    private readonly ProcessingTierQuery _tierQuery;
    private readonly TieredEntityStripeSet _tieredStripeSet;
    private readonly IPlayerQuery _playerQuery;
    private readonly TargetResolution _targetResolution;
    private readonly NpcCorpseLooting _corpseLooting;

    /// <summary>A Health Potion's targeting, for marking the drinker itself.</summary>
    private static readonly IActionActivator HealthPotionActivator = HealthPotion.Build().Activator!;

    private readonly List<Vector3Int> _adjacentTilesBuffer = [];

    public TestCombatBehaviorSystem(
        PackedComponentPool<MovementComponent> movementPool,
        DirectComponentPool<TransformComponent> transformPool,
        PackedComponentPool<ActionLockComponent> actionLocks,
        PackedComponentPool<SimpleHealthComponent> health,
        EntityBodyParts bodyParts,
        MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
        EntityActions actions,
        PackedComponentPool<RaceSlotsComponent> raceSlots,
        PackedComponentPool<PendingActionActivationComponent> pendingActivations,
        PackedComponentPool<PendingItemActivationComponent> pendingItemActivations,
        IMapQuery mapQuery,
        MathUtility mathUtility,
        DirectComponentPool<ProcessingTierComponent> processingTiers,
        ProcessingTierEvents processingTierEvents,
        PackedComponentPool<DeadComponent> deadEntities,
        EffectServices effectServices,
        PackedComponentPool<MeleeDisabledComponent> meleeDisabled,
        IPlayerQuery playerQuery,
        TargetResolution targetResolution,
        NpcCorpseLooting corpseLooting)
    {
        _movementPool = movementPool;
        _transformPool = transformPool;
        _actionLocks = actionLocks;
        _health = health;
        _bodyParts = bodyParts;
        _inventoryStacks = inventoryStacks;
        _actions = actions;
        _raceSlots = raceSlots;
        _pendingActivations = pendingActivations;
        _pendingItemActivations = pendingItemActivations;
        _mapQuery = mapQuery;
        _mathUtility = mathUtility;
        _deadEntities = deadEntities;
        _mana = effectServices.Mana;
        _effectServices = effectServices;
        _statModifiers = effectServices.StatModifiers;
        _meleeDisabled = meleeDisabled;
        _playerQuery = playerQuery;
        _targetResolution = targetResolution;
        _corpseLooting = corpseLooting;

        _tierQuery = new ProcessingTierQuery(processingTiers);
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
    /// otherwise unused, the same way AuraSystem already treats it.
    /// </summary>
    public void Update(EngineTime time, byte stripeIndex) => TieredSystemRunner.Run(this, time);

    public TieredEntityStripeSet Tiers => _tieredStripeSet;

    public void UpdateBucket(EngineTime time, ReadOnlySpan<int> entityIds, ushort framesPerVisit)
    {
        foreach (var entityId in entityIds)
        {
            DecideForEntity(entityId, time.FrameCount);
        }
    }

    /// <summary>One due entity's decision step. Takes no frames-per-visit: this system owns no countdown -- FramesToWait belongs to MovementSystem and is only read here, as a gate. `now` is for the shared action lock, which is a deadline (see ActionLockGate).</summary>
    private void DecideForEntity(int entityId, long now)
    {
        if (_deadEntities.Has(entityId))
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

        // A pure gate, and now so is MovementSystem's matching one: the backoff is a deadline
        // (MovementComponent.WaitUntilFrame), so neither system advances it and the "both
        // decremented the same wait on the same frame, so it elapsed twice as fast" bug that
        // forced a single-owner rule has nothing left to happen to.
        if (movement.IsWaiting(now))
        {
            return;
        }

        if (ActionLockGate.IsBlocked(_actionLocks, entityId, now))
        {
            return;
        }

        if (movement.NextMapPosition is { } pending && pending != transform.Position)
        {
            return; // Still mid-move from a previous decision -- nothing new to decide yet.
        }

        if (TryDecideSelfHeal(entityId)
            || TryDecideMeleeAttack(entityId, transform, now)
            || TryDecideRangedAttackOnPlayer(entityId, transform, now)
            || TryDecideLootCorpse(entityId, transform, now))
        {
            return;
        }

        DecideWander(entityId, transform, now);
    }

    /// <summary>Below half health and holding at least one Health Potion -> drink it. Deliberately simple (a fixed 50% threshold, no smarter "how urgent is this" weighing) -- see this class's own doc comment on why.</summary>
    private bool TryDecideSelfHeal(int entityId)
    {
        if (!HealthQueries.TryGetTotals(_health, _bodyParts, entityId, out var current, out var maximum) || current * 2 >= maximum)
        {
            return false;
        }

        if (!InventoryQueries.TryGetStack(_inventoryStacks, entityId, HealthPotion.Id, out var potionStack) || potionStack.Quantity <= 0)
        {
            return false;
        }

        _pendingItemActivations.Merge(entityId, new PendingItemActivationComponent(potionStack.StackInstanceId, _targetResolution.SelectEntity(entityId, HealthPotionActivator, entityId)));
        ClearStep(entityId);
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
    private bool TryDecideMeleeAttack(int entityId, TransformComponent transform, long now)
    {
        if (!_actions.Has(entityId, QuickAttackAction.Id))
        {
            return false;
        }

        // No race, no notion of "a different race" to attack -- bail before even resolving the
        // footprint. Every real race blueprint fills a race slot, so this only ever fires
        // defensively (this system explicitly isn't goblin-specific, see its own doc comment).
        if (!TryGetRaceId(entityId, out var attackerRaceId))
        {
            return false;
        }

        TargetShapeResolver.Resolve(TargetShape.Adjacent, transform.Position, transform.Size, transform.Position, range: 0, areaSize: 0, _mapQuery.Bounds, _adjacentTilesBuffer);

        if (!HasAttackableNeighbor(_adjacentTilesBuffer, attackerRaceId))
        {
            return false;
        }

        var actionId = _mathUtility.Next(0, 2) == 0 ? QuickAttackAction.Id : PowerAttackAction.Id;
        if (!_actions.TryGetEffectiveAction(entityId, actionId, out var action) ||
            ActivationQueries.GetBlocker(entityId, action, action.Activator, isToggledOn: false, _meleeDisabled, _effectServices, now) != ActivationBlocker.None)
        {
            return false;
        }

        _pendingActivations.Merge(entityId, new PendingActionActivationComponent(actionId, TargetSelection.Ground(transform.Position, action.Activator.Targeting)));
        ClearStep(entityId);
        return true;
    }

    /// <summary>TEMPORARY, with the rest of this system: an entity granted Magic Missile casts it at the player when the player is in range and not adjacent, aiming in Target or Ground mode at random for each cast.</summary>
    /// <remarks>
    /// Only at the player: ranged attacks at any hostile wait on TODO "Spatial queries for NPC
    /// decisions". The mode is drawn from MathUtility, the session's seeded sequence this system
    /// already rolls from, so a seeded run repeats it. Never cast without the mana for it -- the same
    /// blocker check the melee branch makes. Adjacent counts across layers, so a Flying Fairy over the
    /// player doesn't cast at point-blank range.
    /// </remarks>
    private bool TryDecideRangedAttackOnPlayer(int entityId, TransformComponent transform, long now)
    {
        var playerEntityId = _playerQuery.PlayerEntityId;
        if (playerEntityId == entityId ||
            !_actions.TryGetEffectiveAction(entityId, MagicMissileAction.Id, out var action) ||
            !TryGetRaceId(entityId, out var casterRaceId) ||
            !IsAttackable(playerEntityId, casterRaceId) ||
            !_transformPool.TryGetReadonly(playerEntityId, out var playerTransform))
        {
            return false;
        }

        var range = action.Activator.Targeting.Range;
        if (GridDistance.ChebyshevDistance(transform.Position, playerTransform.Position) <= 1 ||
            GridDistance.ManhattanDistance(transform.Position, playerTransform.Position) > range ||
            ActivationQueries.GetBlocker(entityId, action, action.Activator, isToggledOn: false, _meleeDisabled, _effectServices, now) != ActivationBlocker.None)
        {
            return false;
        }

        var selection = _mathUtility.Next(0, 2) == 0
            ? _targetResolution.SelectEntity(entityId, action.Activator, playerEntityId)
            : _targetResolution.Select(entityId, action.Activator, TargetingMode.Ground, playerTransform.Position, now);

        _pendingActivations.Merge(entityId, new PendingActionActivationComponent(MagicMissileAction.Id, selection));
        ClearStep(entityId);
        return true;
    }

    /// <summary>
    /// Checks every occupant of each tile, Blocking or not -- melee is not restricted to
    /// Blocking targets only, so a non-Blocking Fairy/player sharing an adjacent tile still
    /// counts.
    /// </summary>
    private bool HasAttackableNeighbor(List<Vector3Int> adjacentTiles, ushort attackerRaceId)
    {
        foreach (var tile in adjacentTiles)
        {
            foreach (var occupantEntityId in _mapQuery.GetOccupantEntityIdSpanAt(tile))
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
    /// own IMPLEMENTATION-NOTES.md). Also excludes anything frozen: nothing may target across the
    /// simulated/frozen seam (see ProcessingTierQuery). Then requires the candidate to actually
    /// carry a race different from the attacker's own -- a raceless entity (a shop, a container, any
    /// non-creature prop) is never attackable, and two entities of the same race never attack each
    /// other (see this class's own doc comment on why that's now a real comparison, not a
    /// player-or-Fairy allowlist).
    /// </summary>
    private bool IsAttackable(int candidateEntityId, ushort attackerRaceId) =>
        _tierQuery.IsSimulated(candidateEntityId) &&
        !_deadEntities.Has(candidateEntityId) &&
        TryGetRaceId(candidateEntityId, out var candidateRaceId) &&
        candidateRaceId != attackerRaceId;

    /// <summary>The race entityId counts as (its first slot -- see RaceSlotsComponent.Primary), or false if it has no races at all.</summary>
    private bool TryGetRaceId(int entityId, out ushort raceId)
    {
        raceId = _raceSlots.TryGetReadonly(entityId, out var slots) ? slots.Primary : RaceSlotsComponent.Empty;
        return raceId != RaceSlotsComponent.Empty;
    }

    /// <summary>The exact coin-flip-idle-or-move logic MovementSystem's own Random-mode branch used to run directly -- moved here unchanged, now writing NextMapPosition/WaitUntilFrame as this system's own decision rather than MovementSystem deciding and executing in the same call.</summary>
    private void DecideWander(int entityId, TransformComponent transform, long now)
    {
        if (_mathUtility.Next(0, 2) == 0)
        {
            SetIdle(entityId, now);
            return;
        }

        var isBlocking = _mapQuery.IsBlocking(entityId);
        if (MovementCandidates.TryPickRandomAdjacentPosition(_mapQuery, _mathUtility, entityId, transform.Position, transform.Size, isBlocking, out var candidatePosition))
        {
            _movementPool.TryUpdate(entityId, candidatePosition, static (ref MovementComponent m, Vector3Int candidate) => m.NextMapPosition = candidate);
            return;
        }

        SetIdle(entityId, now);
    }

    /// <summary>Loots a corpse on or beside it -- see NpcCorpseLooting. Only once nothing is left to fight, so an adjacent enemy is still dealt with first.</summary>
    private bool TryDecideLootCorpse(int entityId, TransformComponent transform, long now)
    {
        if (!_corpseLooting.TryLootNearbyCorpse(entityId, transform, now))
        {
            return false;
        }

        ClearStep(entityId);
        return true;
    }

    private void ClearStep(int entityId) =>
        _movementPool.TryUpdate(entityId, static (ref MovementComponent m) => m.NextMapPosition = null);

    private void SetIdle(int entityId, long now) =>
        _movementPool.TryUpdate(entityId, FrameDeadline.After(now, MovementCandidates.FramesToWaitIfNoOptions),
            static (ref MovementComponent m, uint waitUntil) => m.WaitUntilFrame = waitUntil);
}
