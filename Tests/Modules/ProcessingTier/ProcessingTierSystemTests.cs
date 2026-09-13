using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.ProcessingTier.Systems;
using Game.World;

namespace Tests.Modules.ProcessingTier;

/// <summary>
/// ProcessingTierSystem is event-driven: entities are tiered at creation, and afterwards only
/// change when an entity moves (drained from the shared move buffer) or the player moves (the
/// Local edges walked through the map's position index). These tests drive both paths through a
/// fake map index, since the edge walk finds entities by tile rather than by scanning.
/// </summary>
[TestClass]
public sealed class ProcessingTierSystemTests
{
    private const int PlayerEntityId = 1;
    private const int OtherEntityId = 0;
    private const int SecondEntityId = 2;

    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    /// <summary>A position index shaped like World's -- one Blocking occupant and one terrain entity per tile -- over a map large enough for Borough/Beyond geometry. Nothing here allocates per tile, so the size is free.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<Vector3Int, int> _blocking = [];
        private readonly Dictionary<Vector3Int, int> _terrain = [];

        public Vector3Int MapSize { get; } = new(6000, 6000, 3);

        public bool IsOnMap(Vector3Int position) =>
            position.X >= 0 && position.Y >= 0 && position.Z >= 0 && position.X < MapSize.X && position.Y < MapSize.Y && position.Z < MapSize.Z;

        public int GetEntityIdAt(Vector3Int position) => _blocking.TryGetValue(position, out var id) ? id : -1;

        public bool IsBlocking(int entityId) => true;

        public int GetTerrainEntityIdAt(Vector3Int position) => _terrain.TryGetValue(position, out var id) ? id : -1;

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) => entityIds.Fill(-1);

        public void SetBlocking(Vector3Int position, int entityId) => _blocking[position] = entityId;

        public void ClearBlocking(Vector3Int position) => _blocking.Remove(position);

        public void SetTerrain(Vector3Int position, int entityId) => _terrain[position] = entityId;
    }

    private sealed class Fixture
    {
        public DirectComponentPool<TransformComponent> Transforms { get; } = new(10, static (ref existing, incoming) => existing = incoming);
        public DirectComponentPool<ProcessingTierComponent> Tiers { get; } = new(10, static (ref existing, incoming) => existing = incoming);
        public ProcessingTierEvents Events { get; } = new();
        public ProcessingTierResolver Resolver { get; } = new();
        public FrameEventBuffer<EntityMovedEvent> MovedEntities { get; } = new();
        public FakeMapQuery Map { get; } = new();
        public ProcessingTierSystem System { get; }

        public Fixture(IPlayerQuery? playerQuery = null)
        {
            Resolver.Wire(Tiers, Transforms, Events);
            System = new ProcessingTierSystem(Transforms, Map, MovedEntities, Resolver, playerQuery ?? new FakePlayerQuery(PlayerEntityId));
        }

        /// <summary>Places a Blocking entity in the transform pool and the fake index, with no tier -- as if created before any reference existed. One Blocking entity per tile, as on the real map -- two on one tile would have the second silently evict the first from the index.</summary>
        public void Place(int entityId, Vector3Int position)
        {
            Transforms.Add(entityId, new TransformComponent(position, new Vector2Byte(1, 1)));
            Map.SetBlocking(position, entityId);
        }

        /// <summary>Moves an entity the way MovementSystem does: transform, map index, and a buffered move record.</summary>
        public void Move(int entityId, Vector3Int newPosition)
        {
            var old = Transforms.GetReadonly(entityId).Position;
            Map.ClearBlocking(old);
            Map.SetBlocking(newPosition, entityId);
            Transforms.TrySet(entityId, new TransformComponent(newPosition, new Vector2Byte(1, 1)));
            MovedEntities.Record(new EntityMovedEvent(entityId, old, newPosition, new Vector2Byte(1, 1)));
        }

        /// <summary>One frame: Update, then clear the buffer, as SystemManager does.</summary>
        public void Frame()
        {
            System.Update(default, 0);
            MovedEntities.ClearFrame();
        }

        public ProcessingTierLevel TierOf(int entityId) => Tiers.GetReadonly(entityId).Tier;
    }

    [TestMethod]
    public void Update_NoPlayerQuery_LeavesEntityUntiered()
    {
        var transforms = new DirectComponentPool<TransformComponent>(10, static (ref existing, incoming) => existing = incoming);
        var tiers = new DirectComponentPool<ProcessingTierComponent>(10, static (ref existing, incoming) => existing = incoming);
        var resolver = new ProcessingTierResolver();
        resolver.Wire(tiers, transforms, new ProcessingTierEvents());
        transforms.Add(OtherEntityId, new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1)));

        var system = new ProcessingTierSystem(transforms, new FakeMapQuery(), new FrameEventBuffer<EntityMovedEvent>(), resolver, playerQuery: null);
        system.Update(default, 0);

        Assert.IsFalse(tiers.Has(OtherEntityId));
    }

    /// <summary>Before the player has *ever* been placed there is genuinely nothing to compute against, so entities stay untiered rather than being tiered against a made-up origin -- and the player is not pinned, since pinning an entity that has never spawned would assert a tier for something that may not exist yet.</summary>
    [TestMethod]
    public void Update_PlayerNeverPlaced_LeavesEntitiesUntiered()
    {
        var fixture = new Fixture();
        fixture.Place(OtherEntityId, new Vector3Int(2, 2, 0));

        fixture.Frame();
        fixture.Frame();

        Assert.IsFalse(fixture.Tiers.Has(OtherEntityId));
        Assert.IsFalse(fixture.Tiers.Has(PlayerEntityId));
    }

    // --- First observation with no reference preset: the full-rebuild safety net. -------------

    [TestMethod]
    public void FirstUpdate_NoPresetReference_TiersEveryPositionedEntity_Local()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(2, 2, 0));
        fixture.Place(OtherEntityId, new Vector3Int(2, 2, 0));

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
    }

    [TestMethod]
    public void FirstUpdate_NoPresetReference_OutsideLocalButSameNeighborhoodCell_Neighborhood()
    {
        // Distance 200, far outside Local, but floor(700/1000) == floor(500/1000): same cell.
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(700, 500, 0));

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
    }

    [TestMethod]
    public void FirstUpdate_NoPresetReference_SameBoroughDifferentNeighborhood_Borough()
    {
        // Neighborhood (0,0) vs (1,0), both borough floor(x/2000) == 0.
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(900, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(1100, 500, 0));

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(OtherEntityId));
    }

    [TestMethod]
    public void FirstUpdate_NoPresetReference_DifferentMapLayer_BeyondRegardlessOfXY()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(2, 2, 0));
        fixture.Place(OtherEntityId, new Vector3Int(2, 2, 1));

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(OtherEntityId));
    }

    [TestMethod]
    public void FirstUpdate_NoPresetReference_FarBeyondAnyRegion_Beyond()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(0, 0, 0));
        fixture.Place(OtherEntityId, new Vector3Int(5000, 5000, 0));

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(OtherEntityId));
    }

    // --- The player. -----------------------------------------------------------------------

    /// <summary>The player is pinned Local on the first update that sees it, and TierChanged fires so every stripe set that already holds it follows.</summary>
    [TestMethod]
    public void FirstUpdate_PlayerIsPinnedLocalAndTierChangedRaised()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(2, 2, 0));

        ProcessingTierLevel? raisedForPlayer = null;
        fixture.Events.TierChanged += (entityId, tier) =>
        {
            if (entityId == PlayerEntityId)
            {
                raisedForPlayer = tier;
            }
        };

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(PlayerEntityId));
        Assert.AreEqual(ProcessingTierLevel.Local, raisedForPlayer);
        Assert.IsTrue(fixture.Resolver.IsPinned(PlayerEntityId));
    }

    /// <summary>Pinned once and never re-examined -- TierChanged must not fire again on later frames, which would churn every consuming stripe set.</summary>
    [TestMethod]
    public void PlayerAlreadyPinned_LaterFrames_DoNotReRaiseTierChanged()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(2, 2, 0));
        fixture.Frame();

        var raisedForPlayer = 0;
        fixture.Events.TierChanged += (entityId, _) =>
        {
            if (entityId == PlayerEntityId)
            {
                raisedForPlayer++;
            }
        };

        fixture.Move(PlayerEntityId, new Vector3Int(3, 2, 0));
        fixture.Frame();
        fixture.Frame();

        Assert.AreEqual(0, raisedForPlayer);
    }

    [TestMethod]
    public void PlayerLeavesTheMap_PlayerRemainsLocal()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Frame();

        Assert.IsTrue(fixture.Transforms.Remove(PlayerEntityId));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(PlayerEntityId));
    }

    /// <summary>
    /// The player leaving the map must not freeze every other entity's tier. A player removed from
    /// the map is returned to the same position, so the retained last-known position stays the
    /// correct reference -- tiers computed against it are right on return, not merely plausible.
    /// </summary>
    [TestMethod]
    public void PlayerLeavesTheMap_OtherEntitiesStillRetierAgainstLastKnownPosition()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(700, 500, 0));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));

        Assert.IsTrue(fixture.Transforms.Remove(PlayerEntityId));
        fixture.Move(OtherEntityId, new Vector3Int(510, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
    }

    // --- Entity moves: the buffered path. ---------------------------------------------------

    [TestMethod]
    public void EntityMovesIntoLocalRadius_IsPromoted()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(700, 500, 0));
        fixture.Frame();

        fixture.Move(OtherEntityId, new Vector3Int(560, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
    }

    [TestMethod]
    public void EntityMovesPastExitRadius_IsDemoted()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(510, 500, 0));
        fixture.Frame();

        fixture.Move(OtherEntityId, new Vector3Int(700, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
    }

    // --- Player moves: the edge walk. ------------------------------------------------------

    /// <summary>Hysteresis: an entity already Local stays Local at distance 90 -- past the entry radius (80), within the exit radius (96).</summary>
    [TestMethod]
    public void PlayerMoves_AlreadyLocal_StaysLocalWithinExitBuffer()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(500, 501, 0));
        fixture.Frame();

        fixture.Move(PlayerEntityId, new Vector3Int(590, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
    }

    /// <summary>Past the exit radius the entity is finally demoted -- found by the demote edge (newly outside radius 96 of the old reference).</summary>
    [TestMethod]
    public void PlayerMoves_AlreadyLocal_DemotedOncePastExitBuffer()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(495, 500, 0));
        fixture.Frame();

        fixture.Move(PlayerEntityId, new Vector3Int(600, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
    }

    /// <summary>
    /// The case the plan singles out, and the reason there are two edges rather than one. An entity
    /// at distance 85 is non-Local (outside 80) but inside the radius-96 box around both the old
    /// and the new reference -- so the symmetric difference of a single radius-96 box pair would
    /// never visit it. The player steps 10 tiles closer (distance 75); the promote edge
    /// (newly inside radius 80) is what must find it.
    /// </summary>
    [TestMethod]
    public void PlayerMoves_Distance85EntityInsideBothExitBoxes_IsStillPromoted()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(585, 500, 0));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId), "Precondition: distance 85 is outside the entry radius.");

        fixture.Move(PlayerEntityId, new Vector3Int(510, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
    }

    /// <summary>A diagonal step exercises both strips of the rectangle difference -- the columns outside the old x-range, and the rows inside it.</summary>
    [TestMethod]
    public void PlayerMovesDiagonally_EntitiesOnBothEdgesArePromoted()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(585, 540, 0));   // leading column
        fixture.Place(SecondEntityId, new Vector3Int(540, 585, 0));  // leading row, inside the old x-range
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(SecondEntityId));

        fixture.Move(PlayerEntityId, new Vector3Int(510, 510, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(SecondEntityId));
    }

    /// <summary>
    /// The bug this rework exists to fix: a stationary entity with no MovementComponent -- here
    /// terrain, as lava is -- used to be permanently Beyond because the old system only tiered
    /// movers. It is found through the map's terrain index and promoted as the player approaches.
    /// </summary>
    [TestMethod]
    public void PlayerApproaches_StationaryTerrainEntity_IsPromoted()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));

        var lavaPosition = new Vector3Int(600, 500, 0);
        fixture.Transforms.Add(OtherEntityId, new TransformComponent(lavaPosition, new Vector2Byte(1, 1)));
        fixture.Map.SetTerrain(lavaPosition, OtherEntityId);
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));

        fixture.Move(PlayerEntityId, new Vector3Int(530, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
    }

    /// <summary>A MapLayer change reclassifies everything: the old layer goes Beyond, the new layer is tiered by distance.</summary>
    [TestMethod]
    public void PlayerChangesMapLayer_FullRebuild()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(505, 500, 0));
        fixture.Place(SecondEntityId, new Vector3Int(505, 500, 1));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(SecondEntityId));

        fixture.Move(PlayerEntityId, new Vector3Int(500, 500, 1));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(OtherEntityId));
        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(SecondEntityId));
    }

    /// <summary>Crossing a Neighborhood cell boundary reclassifies everything too -- Neighborhood is an absolute cell, not a radius.</summary>
    [TestMethod]
    public void PlayerCrossesNeighborhoodBoundary_FullRebuild()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(995, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(1500, 500, 0));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(OtherEntityId), "Precondition: different neighborhood cell, same borough.");

        fixture.Move(PlayerEntityId, new Vector3Int(1005, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
    }

    // --- Spawn reconciliation: reference preset, player lands elsewhere. --------------------

    /// <summary>
    /// The spawn sequence sets the reference to where the player is *aimed* before population, so
    /// entities are born tiered against that. The player then lands on the nearest free cell, which
    /// may differ. The first update must treat the difference as an ordinary player move and walk
    /// the edges -- not full-rebuild, and not ignore it.
    /// </summary>
    [TestMethod]
    public void PresetReference_PlayerLandsElsewhere_EdgeWalkReconciles()
    {
        var fixture = new Fixture();
        fixture.Resolver.SetReferencePosition(new Vector3Int(500, 500, 0));

        // Born tiered against the aimed-at position: distance 85, so Neighborhood.
        var otherPosition = new Vector3Int(585, 500, 0);
        var otherId = fixture.Resolver.CreateEntityAt(new Engine.ECS.Entities.EntityManager(new Engine.ECS.Components.ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10), 10), otherPosition);
        Assert.AreEqual(OtherEntityId, otherId, "Precondition: a fresh EntityManager hands out id 0.");
        fixture.Transforms.Add(OtherEntityId, new TransformComponent(otherPosition, new Vector2Byte(1, 1)));
        fixture.Map.SetBlocking(otherPosition, OtherEntityId);
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));

        // The player actually lands 10 tiles closer.
        fixture.Place(PlayerEntityId, new Vector3Int(510, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
        Assert.AreEqual(new Vector3Int(510, 500, 0), fixture.Resolver.ReferencePosition);
    }
}
