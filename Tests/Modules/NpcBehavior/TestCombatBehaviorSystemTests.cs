using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Modules.Movement;
using Game.Modules.Movement.Components;
using Game.Modules.Movement.Systems;
using Game.Modules.NpcBehavior.Systems;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.Race.Components;
using Game.World;
using Game.Blueprints;
using Game.Spawning;

namespace Tests.Modules.NpcBehavior;

[TestClass]
public sealed class TestCombatBehaviorSystemTests
{
    private const int GoblinEntityId = 0;
    private const int PlayerEntityId = 1;
    private const int OtherGoblinEntityId = 2;

    private const ushort GoblinRace = 1;
    private const ushort HumanRace = 2;
    private const ushort FairyRace = 3;
    private static readonly Vector3Int GoblinPosition = new(5, 5, 0);
    private static readonly Vector3Int AdjacentTile = new(6, 5, 0); // due east of the goblin -- part of Adjacent's 8-neighbor footprint.
    private static readonly Vector2Byte SingleTile = new(1, 1);

    /// <summary>Minimal IMapQuery test double with a configurable Blocking/occupant index -- same shape as ActionEffectResolverTests' own fake.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<Vector3Int, int> _blockingByPosition = [];
        private readonly Dictionary<Vector3Int, List<int>> _occupantsByPosition = [];
        private readonly HashSet<int> _nonBlockingEntities = [];

        public MapBounds Bounds { get; } = new(0, 0, 20, 20, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => !_nonBlockingEntities.Contains(entityId);
        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) { }

        public void SetBlockingOccupant(Vector3Int position, int entityId)
        {
            _blockingByPosition[position] = entityId;
            AddOccupant(position, entityId);
        }

        public void AddNonBlockingOccupant(Vector3Int position, int entityId)
        {
            _nonBlockingEntities.Add(entityId);
            AddOccupant(position, entityId);
        }

        private void AddOccupant(Vector3Int position, int entityId)
        {
            if (!_occupantsByPosition.TryGetValue(position, out var entityIds))
            {
                entityIds = [];
                _occupantsByPosition[position] = entityIds;
            }
            entityIds.Add(entityId);
        }

        public int GetEntityIdAt(Vector3Int position) => _blockingByPosition.TryGetValue(position, out var id) ? id : -1;

        public IReadOnlyList<int> GetOccupantEntityIdsAt(Vector3Int position) =>
            _occupantsByPosition.TryGetValue(position, out var entityIds) ? entityIds : [];
    }

    private sealed record Fixture(
        TestCombatBehaviorSystem System,
        FakeMapQuery MapQuery,
        PackedComponentPool<MovementComponent> MovementPool,
        DirectComponentPool<TransformComponent> TransformPool,
        PackedComponentPool<ActionLockComponent> ActionLockPool,
        PackedComponentPool<SimpleHealthComponent> HealthPool,
        EntityBodyParts BodyParts,
        BodyPartTestWorld BodyPartWorld,
        MultiComponentPool<InventoryItemStackComponent> InventoryStacks,
        MultiComponentPool<ActionInstanceComponent> ActionInstances,
        PackedComponentPool<RaceSlotsComponent> RaceSlots,
        PackedComponentPool<PendingActionActivationComponent> PendingActivations,
        PackedComponentPool<PendingConsumableActivationComponent> PendingConsumableActivations,
        PackedComponentPool<DeadComponent> DeadEntities,
        DirectComponentPool<ProcessingTierComponent> ProcessingTiers,
        MathUtility MathUtility,
        PackedComponentPool<MeleeDisabledComponent> MeleeDisabled);

    private static Fixture Build(MathUtility? mathUtility = null)
    {
        var movementPool = new PackedComponentPool<MovementComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var transformPool = new DirectComponentPool<TransformComponent>(10, static (ref existing, incoming) => existing = incoming);
        var actionLockPool = new PackedComponentPool<ActionLockComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var healthPool = new PackedComponentPool<SimpleHealthComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var bodyPartWorld = new BodyPartTestWorld(new BodyPartTemplate("Head", BodyPartType.Head, 0, ComplexEntityHeadMaximum, IsVital: true));
        var bodyParts = bodyPartWorld.BodyParts;
        var inventoryStacks = new MultiComponentPool<InventoryItemStackComponent>(10, 10);
        var actionInstances = new MultiComponentPool<ActionInstanceComponent>(10, 10);
        var actions = new EntityActions(new ActionCatalog(), new BlueprintRegistry(), actionInstances, new MultiComponentPool<ActionCooldownComponent>(10, 10), EmptyPools.Direct<SpawnRecordComponent>(), EmptyPools.Multi<AppliedBlueprintComponent>());
        var raceSlots = new PackedComponentPool<RaceSlotsComponent>(10, 10, static (ref existing, incoming) =>
        {
            existing.Add(incoming.Race1);
            existing.Add(incoming.Race2);
        });
        var pendingActivations = new PackedComponentPool<PendingActionActivationComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var pendingConsumableActivations = new PackedComponentPool<PendingConsumableActivationComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var deadEntities = new PackedComponentPool<DeadComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var meleeDisabled = new PackedComponentPool<MeleeDisabledComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var mapQuery = new FakeMapQuery();
        var math = mathUtility ?? new MathUtility();

        // Every id these tests use is seeded Local up front, because TieredEntityStripeSet
        // resolves an entity's tier at the moment it joins the driving pool -- and entities join
        // later here, when PlaceGoblin adds their MovementComponent. An entity with no
        // ProcessingTierComponent resolves to Beyond (see ProcessingTierWiring's own doc), whose
        // far coarser cadence would make these single-Update tests silently stop reaching their
        // subject.
        var processingTiers = new DirectComponentPool<ProcessingTierComponent>(10, static (ref existing, incoming) => existing = incoming);
        for (var entityId = 0; entityId < 10; entityId++)
        {
            processingTiers.Add(entityId, new ProcessingTierComponent(ProcessingTierLevel.Local));
        }

        var system = TestSystems.TestCombatBehaviorSystem(
            movementPool, transformPool, actionLockPool, healthPool, bodyParts, inventoryStacks, actions, raceSlots,
            pendingActivations, pendingConsumableActivations, mapQuery, math, processingTiers, new ProcessingTierEvents(), deadEntities, meleeDisabled: meleeDisabled);

        return new Fixture(system, mapQuery, movementPool, transformPool, actionLockPool, healthPool, bodyParts, bodyPartWorld, inventoryStacks, actionInstances, raceSlots, pendingActivations, pendingConsumableActivations, deadEntities, processingTiers, math, meleeDisabled);
    }

    /// <summary>Grants both QuickAttack and PowerAttack, matching every real race blueprint's paired grant -- TryDecideMeleeAttack gates on QuickAttack's presence but randomly picks either for the actual attack.</summary>
    private static void GrantMeleeActions(Fixture fixture, int entityId)
    {
        fixture.ActionInstances.Add(entityId, new ActionInstanceComponent(QuickAttackAction.Id, ActionOverrideEffects.OverrideFlatDamage(QuickAttackAction.Build(), 10)));
        fixture.ActionInstances.Add(entityId, new ActionInstanceComponent(PowerAttackAction.Id, ActionOverrideEffects.OverrideFlatDamage(PowerAttackAction.Build(), 20)));
    }

    private static void PlaceGoblin(Fixture fixture, int entityId, short currentHealth = 200, short maximumHealth = 200, bool grantMeleeActions = true)
    {
        fixture.TransformPool.Add(entityId, new TransformComponent(GoblinPosition, SingleTile));
        fixture.MovementPool.Add(entityId, new MovementComponent(MovementMode.Random, null, null));
        fixture.ActionLockPool.Add(entityId, new ActionLockComponent(standardLockFrames: 10, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        fixture.HealthPool.Add(entityId, new SimpleHealthComponent(currentHealth, maximumHealth));
        // IsAttackable now compares real races -- an attacker with no race slot can never
        // decide anything is "a different race," so TryDecideMeleeAttack bails before even
        // resolving a footprint (see that method's own doc comment).
        fixture.RaceSlots.Add(entityId, new RaceSlotsComponent(GoblinRace));
        if (grantMeleeActions)
        {
            GrantMeleeActions(fixture, entityId);
        }
    }

    /// <summary>Complex-health counterpart to PlaceGoblin -- gives the entity a body plan instead of a SimpleHealthComponent, same shape a Human-race entity would carry.</summary>
    /// <summary>The one body plan the complex-health fixture entity is built on -- a single Head, since these tests only care about its overall health fraction.</summary>
    private const ushort ComplexEntityHeadMaximum = 200;

    private static void PlaceComplexEntity(Fixture fixture, int entityId, float headCurrent, bool grantMeleeActions = true)
    {
        fixture.TransformPool.Add(entityId, new TransformComponent(GoblinPosition, SingleTile));
        fixture.MovementPool.Add(entityId, new MovementComponent(MovementMode.Random, null, null));
        fixture.ActionLockPool.Add(entityId, new ActionLockComponent(standardLockFrames: 10, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        fixture.BodyPartWorld.Give(entityId);
        fixture.BodyParts.SetCurrentHealth(entityId, 0, headCurrent);
        if (grantMeleeActions)
        {
            GrantMeleeActions(fixture, entityId);
        }
    }

    [TestMethod]
    public void Update_BelowHalfHealthWithPotion_QueuesSelfHeal_NotAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId, currentHealth: 50, maximumHealth: 200);
        fixture.InventoryStacks.Add(GoblinEntityId, new InventoryItemStackComponent(HealthPotion.Id, quantity: 1));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingConsumableActivations.Has(GoblinEntityId));
        var pending = fixture.PendingConsumableActivations.GetReadonly(GoblinEntityId);
        var pool = fixture.InventoryStacks;
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(pool, GoblinEntityId, pending.StackInstanceId, out var boundStack));
        Assert.AreEqual(HealthPotion.Id, boundStack.ItemDefinitionId);
        Assert.HasCount(1, pending.TargetTiles);
        Assert.AreEqual(GoblinPosition, pending.TargetTiles[0]);
        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId), "Healing takes priority over attacking -- both should never fire the same tick.");
    }

    [TestMethod]
    public void Update_BelowHalfHealthButNoPotion_FallsThroughToAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId, currentHealth: 50, maximumHealth: 200);
        fixture.RaceSlots.Add(PlayerEntityId, new RaceSlotsComponent(HumanRace));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingConsumableActivations.Has(GoblinEntityId));
        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId));
    }

    /// <summary>Complex-health counterpart to Update_BelowHalfHealthWithPotion_QueuesSelfHeal_NotAttack -- proves TryDecideSelfHeal's HealthQueries.TryGetTotals fix actually reads a Complex entity's summed total instead of always returning false the way the old direct SimpleHealthComponent read did.</summary>
    [TestMethod]
    public void Update_ComplexEntityBelowHalfHealthWithPotion_QueuesSelfHeal_NotAttack()
    {
        var fixture = Build();
        PlaceComplexEntity(fixture, GoblinEntityId, headCurrent: 50);
        fixture.InventoryStacks.Add(GoblinEntityId, new InventoryItemStackComponent(HealthPotion.Id, quantity: 1));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingConsumableActivations.Has(GoblinEntityId));
        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId), "Healing takes priority over attacking -- both should never fire the same tick.");
    }

    [TestMethod]
    public void Update_FullHealthAdjacentToPlayer_QueuesMeleeAttackAgainstWholeAdjacentFootprint()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        fixture.RaceSlots.Add(PlayerEntityId, new RaceSlotsComponent(HumanRace));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId));
        var pending = fixture.PendingActivations.GetReadonly(GoblinEntityId);
        Assert.IsTrue(pending.ActionId == QuickAttackAction.Id || pending.ActionId == PowerAttackAction.Id, "Randomly one or the other -- see TryDecideMeleeAttack's own doc comment.");
        Assert.HasCount(8, pending.TargetTiles, "The whole resolved Adjacent footprint is queued, not just the occupied tile -- ActionEffectResolver sorts out who's actually there.");
        CollectionAssert.Contains(pending.TargetTiles, AdjacentTile);
        CollectionAssert.DoesNotContain(pending.TargetTiles, GoblinPosition);
    }

    [TestMethod]
    public void Update_MeleeDisabledAdjacentToPlayer_QueuesNoAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        fixture.MeleeDisabled.Add(GoblinEntityId, new MeleeDisabledComponent());
        fixture.RaceSlots.Add(PlayerEntityId, new RaceSlotsComponent(HumanRace));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId));
    }

    [TestMethod]
    public void Update_AdjacentToAnotherGoblin_DoesNeitherHealNorAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        fixture.RaceSlots.Add(OtherGoblinEntityId, new RaceSlotsComponent(GoblinRace));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, OtherGoblinEntityId);
        // Same race as the attacker -- IsAttackable's race-mismatch check correctly excludes it.

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId));
        Assert.IsFalse(fixture.PendingConsumableActivations.Has(GoblinEntityId));
    }

    /// <summary>A raceless entity (a shop, a container, any non-creature prop) is never attackable -- IsAttackable requires the candidate to actually hold a race to compare against, not just "any race but mine."</summary>
    [TestMethod]
    public void Update_AdjacentToRacelessEntity_DoesNotAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        const int racelessEntityId = 3;
        // No race slot registered for racelessEntityId at all.
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, racelessEntityId);

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId));
    }

    [TestMethod]
    public void Update_AdjacentToNonBlockingFairy_StillQueuesAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        const int fairyEntityId = 3;
        fixture.RaceSlots.Add(fairyEntityId, new RaceSlotsComponent(FairyRace));
        fixture.MapQuery.AddNonBlockingOccupant(AdjacentTile, fairyEntityId);

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId), "Melee is not restricted to Blocking targets only -- a non-Blocking Fairy sharing an adjacent tile still counts.");
    }

    /// <summary>
    /// A dead Fairy's corpse stays fully populated and occupying its tile for future looting
    /// (DeathSystem never destroys the entity) -- without IsAttackable's own dead check, it would
    /// still read as a valid melee target here even though it's a corpse.
    /// </summary>
    [TestMethod]
    public void Update_AdjacentToDeadFairy_DoesNotAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        const int deadFairyEntityId = 3;
        fixture.RaceSlots.Add(deadFairyEntityId, new RaceSlotsComponent(FairyRace));
        fixture.DeadEntities.Add(deadFairyEntityId, new DeadComponent(KilledBy: ActionSource.Admin, DiedAtFrame: 0));
        fixture.MapQuery.AddNonBlockingOccupant(AdjacentTile, deadFairyEntityId);

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId), "A dead Fairy's corpse is not a valid melee target.");
    }

    /// <summary>The seam: a frozen neighbor resolves nothing and cannot fight back, so nothing targets it.</summary>
    [TestMethod]
    public void Update_AdjacentToFrozenFairy_DoesNotAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        const int frozenFairyEntityId = 3;
        fixture.RaceSlots.Add(frozenFairyEntityId, new RaceSlotsComponent(FairyRace));
        fixture.ProcessingTiers.TrySet(frozenFairyEntityId, new ProcessingTierComponent(ProcessingTierLevel.Borough));
        fixture.MapQuery.AddNonBlockingOccupant(AdjacentTile, frozenFairyEntityId);

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId), "A frozen Fairy is not a valid melee target.");
    }

    [TestMethod]
    public void Update_ActionLocked_SkipsEntirely_WithoutTouchingHealthOrInventory()
    {
        var fixture = Build();
        fixture.TransformPool.Add(GoblinEntityId, new TransformComponent(GoblinPosition, SingleTile));
        fixture.MovementPool.Add(GoblinEntityId, new MovementComponent(MovementMode.Random, null, null));
        fixture.ActionLockPool.Add(GoblinEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, unlockedAtFrame: 30));
        // Deliberately no SimpleHealthComponent/InventoryItemStackComponent/ActionInstanceComponent
        // registered for this entity -- if the system tried to read any of them before checking
        // the action lock, this would throw or behave unexpectedly instead of just skipping.

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId));
        Assert.IsFalse(fixture.PendingConsumableActivations.Has(GoblinEntityId));
    }

    [TestMethod]
    public void Update_DecisionQueuedThisTick_PreventsMovementSystemFromAlsoMovingSameEntitySameFrame()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        fixture.RaceSlots.Add(PlayerEntityId, new RaceSlotsComponent(HumanRace));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);
        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId), "Sanity check: the goblin decided to attack this tick.");

        var eventBus = new Engine.Events.EventBus();
        var movementSystem = TestSystems.MovementSystem(
            fixture.TransformPool, fixture.ActionLockPool, fixture.MovementPool, fixture.MapQuery, eventBus,
            new RecordingEntityMoveSync(), new Engine.ECS.Systems.FrameEventBuffer<EntityMovedEvent>(), null,
            new DirectComponentPool<ProcessingTierComponent>(10, static (ref existing, incoming) => existing = incoming),
            new ProcessingTierEvents());

        movementSystem.Update(default, 0);

        Assert.AreEqual(GoblinPosition, fixture.TransformPool.GetReadonly(GoblinEntityId).Position, "A goblin that queued an attack this tick must not also move.");
    }

    [TestMethod]
    public void Update_QueuingAnAttack_ClearsAnArrivedStep()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        fixture.MovementPool.TryUpdate(GoblinEntityId, static (ref MovementComponent m) => m.NextMapPosition = GoblinPosition);
        fixture.RaceSlots.Add(PlayerEntityId, new RaceSlotsComponent(HumanRace));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId));
        Assert.IsNull(fixture.MovementPool.GetReadonly(GoblinEntityId).NextMapPosition);
    }

    [TestMethod]
    public void Update_QueuingASelfHeal_ClearsAnArrivedStep()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId, currentHealth: 50, maximumHealth: 200);
        fixture.MovementPool.TryUpdate(GoblinEntityId, static (ref MovementComponent m) => m.NextMapPosition = GoblinPosition);
        fixture.InventoryStacks.Add(GoblinEntityId, new InventoryItemStackComponent(HealthPotion.Id, quantity: 1));

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingConsumableActivations.Has(GoblinEntityId));
        Assert.IsNull(fixture.MovementPool.GetReadonly(GoblinEntityId).NextMapPosition);
    }

    private sealed class RecordingEntityMoveSync : IEntityMoveSync
    {
        public void SyncMove(EntityMovedEvent moved, bool isBlocking) { }
        public void ConvertToNonBlocking(int entityId, ref TransformComponent transform) { }
    }
}
