using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Blueprints.Races;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
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

namespace Tests.Modules.NpcBehavior;

[TestClass]
public sealed class TestCombatBehaviorSystemTests
{
    private const int GoblinEntityId = 0;
    private const int PlayerEntityId = 1;
    private const int OtherGoblinEntityId = 2;
    private static readonly Vector3Int GoblinPosition = new(5, 5, 0);
    private static readonly Vector3Int AdjacentTile = new(6, 5, 0); // due east of the goblin -- part of Adjacent's 8-neighbor footprint.
    private static readonly Vector2Byte SingleTile = new(1, 1);

    /// <summary>Minimal IMapQuery test double with a configurable Blocking/occupant index -- same shape as ActionEffectResolverTests' own fake.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<Vector3Int, int> _blockingByPosition = [];
        private readonly Dictionary<Vector3Int, List<int>> _occupantsByPosition = [];
        private readonly HashSet<int> _nonBlockingEntities = [];

        public Vector3Int MapSize { get; } = new(20, 20, 1);
        public bool IsOnMap(Vector3Int position) => true;
        public bool IsBlocking(int entityId) => !_nonBlockingEntities.Contains(entityId);
        public int GetTerrainEntityIdAt(Vector3Int position) => -1;
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

    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private sealed record Fixture(
        TestCombatBehaviorSystem System,
        FakeMapQuery MapQuery,
        PackedComponentPool<MovementComponent> MovementPool,
        DirectComponentPool<TransformComponent> TransformPool,
        PackedComponentPool<ActionLockComponent> ActionLockPool,
        PackedComponentPool<SimpleHealthComponent> HealthPool,
        MultiComponentPool<BodyPartComponent> BodyParts,
        MultiComponentPool<InventoryItemStackComponent> InventoryStacks,
        MultiComponentPool<ActionInstanceComponent> ActionInstances,
        MultiComponentPool<RaceComponent> RaceComponents,
        PackedComponentPool<PendingActionActivationComponent> PendingActivations,
        PackedComponentPool<PendingConsumableActivationComponent> PendingConsumableActivations,
        MathUtility MathUtility);

    private static Fixture Build(int playerEntityId = PlayerEntityId, MathUtility? mathUtility = null)
    {
        var movementPool = new PackedComponentPool<MovementComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var transformPool = new DirectComponentPool<TransformComponent>(10, static (ref existing, incoming) => existing = incoming);
        var actionLockPool = new PackedComponentPool<ActionLockComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var healthPool = new PackedComponentPool<SimpleHealthComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var bodyParts = new MultiComponentPool<BodyPartComponent>(10, 10);
        var inventoryStacks = new MultiComponentPool<InventoryItemStackComponent>(10, 10);
        var actionInstances = new MultiComponentPool<ActionInstanceComponent>(10, 10);
        var raceComponents = new MultiComponentPool<RaceComponent>(10, 10);
        var pendingActivations = new PackedComponentPool<PendingActionActivationComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var pendingConsumableActivations = new PackedComponentPool<PendingConsumableActivationComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var mapQuery = new FakeMapQuery();
        var math = mathUtility ?? new MathUtility();

        var system = new TestCombatBehaviorSystem(
            movementPool, transformPool, actionLockPool, healthPool, bodyParts, inventoryStacks, actionInstances, raceComponents,
            pendingActivations, pendingConsumableActivations, mapQuery, math, new FakePlayerQuery(playerEntityId));

        return new Fixture(system, mapQuery, movementPool, transformPool, actionLockPool, healthPool, bodyParts, inventoryStacks, actionInstances, raceComponents, pendingActivations, pendingConsumableActivations, math);
    }

    private static void PlaceGoblin(Fixture fixture, int entityId, short currentHealth = 200, short maximumHealth = 200, bool grantPunch = true)
    {
        fixture.TransformPool.Add(entityId, new TransformComponent(GoblinPosition, SingleTile));
        fixture.MovementPool.Add(entityId, new MovementComponent(MovementMode.Random, null, null));
        fixture.ActionLockPool.Add(entityId, new ActionLockComponent(standardLockFrames: 10, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        fixture.HealthPool.Add(entityId, new SimpleHealthComponent(currentHealth, maximumHealth));
        if (grantPunch)
        {
            fixture.ActionInstances.Add(entityId, new ActionInstanceComponent(PunchAction.Id, damageAmount: 10, cooldownFramesRemaining: 0));
        }
    }

    /// <summary>Complex-health counterpart to PlaceGoblin -- grants BodyPartComponents instead of a SimpleHealthComponent, same shape a Human-race entity would carry.</summary>
    private static void PlaceComplexEntity(Fixture fixture, int entityId, float headCurrent, float headMaximum, bool grantPunch = true)
    {
        fixture.TransformPool.Add(entityId, new TransformComponent(GoblinPosition, SingleTile));
        fixture.MovementPool.Add(entityId, new MovementComponent(MovementMode.Random, null, null));
        fixture.ActionLockPool.Add(entityId, new ActionLockComponent(standardLockFrames: 10, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        fixture.BodyParts.Add(entityId, new BodyPartComponent("Head", BodyPartType.Head, 0, 0, headCurrent, headMaximum, isVital: true));
        if (grantPunch)
        {
            fixture.ActionInstances.Add(entityId, new ActionInstanceComponent(PunchAction.Id, damageAmount: 10, cooldownFramesRemaining: 0));
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
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingConsumableActivations.Has(GoblinEntityId));
        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId));
    }

    /// <summary>Complex-health counterpart to Update_BelowHalfHealthWithPotion_QueuesSelfHeal_NotAttack -- proves TryDecideSelfHeal's HealthQueries.TryGetTotals fix (PLAN-human-race.md) actually reads a Complex entity's summed total instead of always returning false the way the old direct SimpleHealthComponent read did.</summary>
    [TestMethod]
    public void Update_ComplexEntityBelowHalfHealthWithPotion_QueuesSelfHeal_NotAttack()
    {
        var fixture = Build();
        PlaceComplexEntity(fixture, GoblinEntityId, headCurrent: 50, headMaximum: 200);
        fixture.InventoryStacks.Add(GoblinEntityId, new InventoryItemStackComponent(HealthPotion.Id, quantity: 1));
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingConsumableActivations.Has(GoblinEntityId));
        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId), "Healing takes priority over attacking -- both should never fire the same tick.");
    }

    [TestMethod]
    public void Update_FullHealthAdjacentToPlayer_QueuesPunchAgainstWholeAdjacentFootprint()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId));
        var pending = fixture.PendingActivations.GetReadonly(GoblinEntityId);
        Assert.AreEqual(PunchAction.Id, pending.ActionId);
        Assert.HasCount(8, pending.TargetTiles, "The whole resolved Adjacent footprint is queued, not just the occupied tile -- ActionEffectResolver sorts out who's actually there.");
        CollectionAssert.Contains(pending.TargetTiles, AdjacentTile);
        CollectionAssert.DoesNotContain(pending.TargetTiles, GoblinPosition);
    }

    [TestMethod]
    public void Update_AdjacentToAnotherGoblin_DoesNeitherHealNorAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, OtherGoblinEntityId);
        // OtherGoblinEntityId has no RaceComponent registered at all -- IsFairy correctly reports false, and it's not the configured player either.

        fixture.System.Update(default, 0);

        Assert.IsFalse(fixture.PendingActivations.Has(GoblinEntityId));
        Assert.IsFalse(fixture.PendingConsumableActivations.Has(GoblinEntityId));
    }

    [TestMethod]
    public void Update_AdjacentToNonBlockingFairy_StillQueuesAttack()
    {
        var fixture = Build();
        PlaceGoblin(fixture, GoblinEntityId);
        const int fairyEntityId = 3;
        fixture.RaceComponents.Add(fairyEntityId, new RaceComponent(Fairy.RaceId, "Fairy", "A fairy."));
        fixture.MapQuery.AddNonBlockingOccupant(AdjacentTile, fairyEntityId);

        fixture.System.Update(default, 0);

        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId), "Melee is not restricted to Blocking targets only -- a non-Blocking Fairy sharing an adjacent tile still counts.");
    }

    [TestMethod]
    public void Update_ActionLocked_SkipsEntirely_WithoutTouchingHealthOrInventory()
    {
        var fixture = Build();
        fixture.TransformPool.Add(GoblinEntityId, new TransformComponent(GoblinPosition, SingleTile));
        fixture.MovementPool.Add(GoblinEntityId, new MovementComponent(MovementMode.Random, null, null));
        fixture.ActionLockPool.Add(GoblinEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 30, currentLockFramesRemaining: 30));
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
        fixture.MapQuery.SetBlockingOccupant(AdjacentTile, PlayerEntityId);

        fixture.System.Update(default, 0);
        Assert.IsTrue(fixture.PendingActivations.Has(GoblinEntityId), "Sanity check: the goblin decided to attack this tick.");

        var eventBus = new Engine.Events.EventBus();
        var movementSystem = new MovementSystem(
            fixture.TransformPool, fixture.ActionLockPool, fixture.MovementPool, fixture.MapQuery, eventBus,
            new RecordingEntityMoveSync(), new Engine.ECS.Systems.FrameEventBuffer<EntityMovedEvent>(), null,
            new DirectComponentPool<ProcessingTierComponent>(10, static (ref existing, incoming) => existing = incoming),
            new ProcessingTierEvents(), pendingActionActivations: fixture.PendingActivations, pendingConsumableActivations: fixture.PendingConsumableActivations);

        movementSystem.Update(default, 0);

        Assert.AreEqual(GoblinPosition, fixture.TransformPool.GetReadonly(GoblinEntityId).Position, "MovementSystem must see this tick's queued attack and skip moving the goblin entirely.");
    }

    private sealed class RecordingEntityMoveSync : IEntityMoveSync
    {
        public void SyncMove(EntityMovedEvent moved, bool isBlocking) { }
        public void ConvertToNonBlocking(int entityId, ref TransformComponent transform) { }
    }
}
