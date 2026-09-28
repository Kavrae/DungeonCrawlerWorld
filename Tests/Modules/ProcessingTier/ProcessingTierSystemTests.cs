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

    /// <summary>A position index shaped like World's -- one Blocking occupant per tile -- over a map large enough for Borough/Beyond geometry, reaching into negative coordinates. Nothing here allocates per tile, so the size is free.</summary>
    private sealed class FakeMapQuery : IMapQuery
    {
        private readonly Dictionary<Vector3Int, int> _blocking = [];

        public MapBounds Bounds { get; } = new(-3072, -3072, 6000, 6000, 3);

        public bool IsOnMap(Vector3Int position) => Bounds.Contains(position);

        public int GetEntityIdAt(Vector3Int position) => _blocking.TryGetValue(position, out var id) ? id : -1;

        public bool IsBlocking(int entityId) => true;

        public void GetEntityIdsInBox(CubeInt box, Span<int> entityIds) => entityIds.Fill(-1);

        public void SetBlocking(Vector3Int position, int entityId) => _blocking[position] = entityId;

        public void ClearBlocking(Vector3Int position) => _blocking.Remove(position);

        public HashSet<(int CellX, int CellY)> UnloadedNeighborhoods { get; } = [];

        public bool IsNeighborhoodLoaded(int cellX, int cellY) => !UnloadedNeighborhoods.Contains((cellX, cellY));
    }

    private sealed class Fixture
    {
        public DirectComponentPool<TransformComponent> Transforms { get; } = new(10, static (ref existing, incoming) => existing = incoming);
        public DirectComponentPool<ProcessingTierComponent> Tiers { get; } = new(10, static (ref existing, incoming) => existing = incoming);
        public ProcessingTierEvents Events { get; } = new();
        public ProcessingTierResolver Resolver { get; }
        public FrameEventBuffer<EntityMovedEvent> MovedEntities { get; } = new();
        public FakeMapQuery Map { get; } = new();
        public ProcessingTierSystem System { get; }

        public Fixture(IPlayerQuery? playerQuery = null, int transitionsPerFrame = ProcessingTierSystem.DefaultTransitionsPerFrame)
        {
            Resolver = new ProcessingTierResolver(Tiers, Transforms, Events);
            System = new ProcessingTierSystem(Transforms, Map, MovedEntities, Resolver, playerQuery ?? new TestPlayerQuery(PlayerEntityId), transitionsPerFrame);
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

        public Engine.ECS.Entities.EntityManager Entities { get; } = new(new Engine.ECS.Components.ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10), 10);

        /// <summary>Creates a Blocking entity the way population does against a preset reference: tiered and indexed at creation through the resolver, so no first-frame scan ever sees it.</summary>
        public int Spawn(Vector3Int position)
        {
            var entityId = Resolver.CreateEntityAt(Entities, position);
            Transforms.Add(entityId, new TransformComponent(position, new Vector2Byte(1, 1)));
            Map.SetBlocking(position, entityId);
            return entityId;
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
    public void Update_NoPlayer_LeavesEntityUntiered()
    {
        var transforms = new DirectComponentPool<TransformComponent>(10, static (ref existing, incoming) => existing = incoming);
        var tiers = new DirectComponentPool<ProcessingTierComponent>(10, static (ref existing, incoming) => existing = incoming);
        var resolver = new ProcessingTierResolver(tiers, transforms, new ProcessingTierEvents());
        transforms.Add(OtherEntityId, new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1)));

        var system = new ProcessingTierSystem(transforms, new FakeMapQuery(), new FrameEventBuffer<EntityMovedEvent>(), resolver, playerQuery: TestPlayerQuery.NoPlayer);
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
    public void FirstUpdate_NoPresetReference_OutsideLocalButSameNeighborhood_Neighborhood()
    {
        // Distance 200, far outside Local, but both in neighborhood (0, 0).
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(700, 500, 0));

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
    }

    [TestMethod]
    public void FirstUpdate_NoPresetReference_AdjacentNeighborhood_Borough()
    {
        // Neighborhood (0,0) vs (1,0): adjacent.
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(900, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(1100, 500, 0));

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(OtherEntityId));
    }

    [TestMethod]
    public void FirstUpdate_NoPresetReference_DifferentMapLayerWithinLocalRadius_NeighborhoodNotLocal()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(2, 2, 0));
        fixture.Place(OtherEntityId, new Vector3Int(2, 2, 1));

        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
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

    /// <summary>A MapLayer change moves Local with the player: what was Local on the old layer drops to its cell's tier, and what is near on the new layer becomes Local.</summary>
    [TestMethod]
    public void PlayerChangesMapLayer_LocalMovesToTheNewLayer()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(500, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(505, 500, 0));
        fixture.Place(SecondEntityId, new Vector3Int(505, 500, 1));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(OtherEntityId));
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(SecondEntityId));

        fixture.Move(PlayerEntityId, new Vector3Int(500, 500, 1));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(SecondEntityId));
    }

    /// <summary>Crossing into another neighborhood reclassifies the old and new ones -- Neighborhood is a whole neighborhood, not a radius.</summary>
    [TestMethod]
    public void PlayerCrossesNeighborhoodBoundary_RetiersNewCell()
    {
        var fixture = new Fixture();
        fixture.Place(PlayerEntityId, new Vector3Int(1019, 500, 0));
        fixture.Place(OtherEntityId, new Vector3Int(1500, 500, 0));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(OtherEntityId), "Precondition: an adjacent neighborhood.");

        fixture.Move(PlayerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(OtherEntityId));
    }

    // --- Cell crossings against a preset reference: found through the membership index. ------

    /// <summary>A fixture whose reference is preset and whose player is the first spawned entity (id 0), so every entity is tiered and indexed at creation and no full scan ever runs.</summary>
    private static Fixture PresetFixture(Vector3Int playerPosition, out int playerEntityId)
    {
        var fixture = new Fixture(new TestPlayerQuery(0));
        fixture.Resolver.SetReferencePosition(playerPosition);
        playerEntityId = fixture.Spawn(playerPosition);
        Assert.AreEqual(0, playerEntityId, "Precondition: the player is the first entity created.");
        fixture.Frame();
        return fixture;
    }

    [TestMethod]
    public void PresetReference_PlayerCrossesNeighborhoodBoundary_EntityInNewCellIsPromotedToNeighborhood()
    {
        var fixture = PresetFixture(new Vector3Int(1019, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(1500, 500, 0));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(otherEntityId), "Precondition: an adjacent neighborhood.");

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(otherEntityId));
    }

    [TestMethod]
    public void PresetReference_PlayerCrossesNeighborhoodBoundary_EntityInOldCellIsDemotedToBorough()
    {
        var fixture = PresetFixture(new Vector3Int(1029, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(1500, 500, 0));
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(otherEntityId), "Precondition: the same neighborhood.");

        fixture.Move(playerEntityId, new Vector3Int(1019, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(otherEntityId));
    }

    /// <summary>A crossing shifts the whole ring, not just the two neighborhoods the player stepped between: a neighborhood two away from the old one joins the ring, and one two away from the new one leaves it.</summary>
    [TestMethod]
    public void PresetReference_PlayerCrossesNeighborhoodBoundary_NeighborhoodJoiningTheRingIsPromotedToBorough()
    {
        var fixture = PresetFixture(new Vector3Int(2043, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(3500, 1500, 0));
        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(otherEntityId), "Precondition: two neighborhoods away.");

        fixture.Move(playerEntityId, new Vector3Int(2053, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(otherEntityId));
    }

    [TestMethod]
    public void PresetReference_PlayerCrossesNeighborhoodBoundary_NeighborhoodLeavingTheRingIsDemotedToBeyond()
    {
        var fixture = PresetFixture(new Vector3Int(2043, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(500, 1500, 0));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(otherEntityId), "Precondition: an adjacent neighborhood.");

        fixture.Move(playerEntityId, new Vector3Int(2053, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(otherEntityId));
    }

    [TestMethod]
    public void PresetReference_PlayerCrossesDiagonally_OppositeCornerJoinsTheRing_SharedRingNeighborhoodStaysBorough()
    {
        var fixture = PresetFixture(new Vector3Int(1019, 1019, 0), out var playerEntityId);
        var joining = fixture.Spawn(new Vector3Int(3000, 100, 0));
        var shared = fixture.Spawn(new Vector3Int(100, 1500, 0));
        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(joining), "Precondition: two neighborhoods away.");
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(shared), "Precondition: adjacent.");

        fixture.Move(playerEntityId, new Vector3Int(1029, 1029, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(joining));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(shared));
    }

    /// <summary>A jump to a neighborhood far from the old one retiers both 3x3s, which no longer overlap at all.</summary>
    [TestMethod]
    public void PresetReference_PlayerJumpsFarAway_BothWindowsAreRetiered()
    {
        var fixture = PresetFixture(new Vector3Int(500, 500, 0), out var playerEntityId);
        var oldCenter = fixture.Spawn(new Vector3Int(700, 500, 1));
        var oldRing = fixture.Spawn(new Vector3Int(1500, 500, 0));
        var newRing = fixture.Spawn(new Vector3Int(4500, 500, 0));
        var newCenter = fixture.Spawn(new Vector3Int(5700, 500, 2));

        fixture.Move(playerEntityId, new Vector3Int(5500, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(oldCenter));
        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(oldRing));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(newRing));
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(newCenter));
    }

    /// <summary>The index follows entity moves: an entity that walked into the new cell after it was created is found there, not in the cell it was born in.</summary>
    [TestMethod]
    public void PresetReference_EntityMovedIntoCellBeforeCrossing_IsFoundInItsNewCell()
    {
        var fixture = PresetFixture(new Vector3Int(1019, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(2500, 500, 0));
        fixture.Move(otherEntityId, new Vector3Int(1500, 500, 0));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(otherEntityId), "Precondition: moved into a neighborhood adjacent to the player's.");

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(otherEntityId));
    }

    [TestMethod]
    public void PresetReference_PlayerChangesMapLayer_NearbyEntityOnNewLayerIsPromotedToLocal()
    {
        var fixture = PresetFixture(new Vector3Int(500, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(590, 500, 1));
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(otherEntityId), "Precondition: a different MapLayer is never Local.");

        fixture.Move(playerEntityId, new Vector3Int(510, 500, 1));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(otherEntityId));
    }

    /// <summary>The demote side of a layer change is found by the exit-radius square around the old reference -- here an entity at distance 90, past the entry radius but still Local through hysteresis.</summary>
    [TestMethod]
    public void PresetReference_PlayerChangesMapLayer_LocalEntityOnOldLayerIsDemoted()
    {
        var fixture = PresetFixture(new Vector3Int(500, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(570, 500, 0));
        fixture.Move(otherEntityId, new Vector3Int(590, 500, 0));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(otherEntityId), "Precondition: Local through hysteresis at distance 90.");

        fixture.Move(playerEntityId, new Vector3Int(500, 500, 1));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(otherEntityId));
    }

    /// <summary>Neighborhood and Borough ignore MapLayer, so a neighborhood crossing retiers entities on every layer, not only the player's.</summary>
    [TestMethod]
    public void PresetReference_PlayerCrossesNeighborhoodBoundary_EntityOnAnotherLayerIsRetiered()
    {
        var fixture = PresetFixture(new Vector3Int(1019, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(1500, 500, 2));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(otherEntityId), "Precondition: an adjacent neighborhood, another layer.");

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(otherEntityId));
    }

    /// <summary>An entity destroyed without the index hearing about it is dropped when its cell is next walked, rather than retiered or thrown on.</summary>
    [TestMethod]
    public void PresetReference_DestroyedEntityInWalkedCell_IsDroppedFromIndex()
    {
        var fixture = PresetFixture(new Vector3Int(1019, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(1500, 500, 0));
        Assert.IsTrue(fixture.Transforms.Remove(otherEntityId));
        var indexedBefore = fixture.Resolver.Membership.Count;

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();

        Assert.AreEqual(indexedBefore - 1, fixture.Resolver.Membership.Count);
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

    // --- The smeared transition queue. -------------------------------------------------------

    /// <summary>A crossing queues the neighborhoods whose tier changed rather than retiering them in that frame, so the work is spread over as many frames as the budget needs.</summary>
    [TestMethod]
    public void PresetReference_Crossing_RetiersOnlyTheBudgetPerFrame()
    {
        var fixture = new Fixture(new TestPlayerQuery(0), transitionsPerFrame: 1);
        fixture.Resolver.SetReferencePosition(new Vector3Int(1019, 500, 0));
        var playerEntityId = fixture.Spawn(new Vector3Int(1019, 500, 0));
        var first = fixture.Spawn(new Vector3Int(1500, 500, 0));
        var second = fixture.Spawn(new Vector3Int(1600, 500, 0));
        fixture.Frame();

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();

        Assert.AreEqual(1, new[] { first, second }.Count(id => fixture.TierOf(id) == ProcessingTierLevel.Neighborhood), "Exactly one of the two was recomputed this frame.");
        Assert.IsTrue(fixture.Resolver.Transitions.HasPending);

        for (var i = 0; i < 200 && fixture.Resolver.Transitions.HasPending; i++)
        {
            fixture.Frame();
        }

        Assert.IsFalse(fixture.Resolver.Transitions.HasPending);
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(first));
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(second));
    }

    /// <summary>While the resolver holds promotions, a crossing's thawing waits and nothing else does; releasing the hold thaws it.</summary>
    [TestMethod]
    public void PresetReference_Crossing_WhilePromotionsHeld_ThawsNothingUntilReleased()
    {
        var held = true;
        var fixture = new Fixture(new TestPlayerQuery(0), transitionsPerFrame: 1);
        fixture.Resolver.PromotionsHeld = () => held;
        fixture.Resolver.SetReferencePosition(new Vector3Int(1019, 500, 0));
        var playerEntityId = fixture.Spawn(new Vector3Int(1019, 500, 0));
        var freezing = fixture.Spawn(new Vector3Int(500, 500, 0));
        var thawing = fixture.Spawn(new Vector3Int(1500, 500, 0));
        fixture.Frame();

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        for (var i = 0; i < 20; i++)
        {
            fixture.Frame();
        }

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(freezing));
        Assert.AreNotEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(thawing));
        Assert.IsTrue(fixture.Resolver.Transitions.HasPending);

        held = false;
        for (var i = 0; i < 200 && fixture.Resolver.Transitions.HasPending; i++)
        {
            fixture.Frame();
        }

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(thawing));
    }

    /// <summary>Thawing runs ahead of freezing: the neighborhood the player walked into comes alive before the one behind them is frozen.</summary>
    [TestMethod]
    public void PresetReference_Crossing_ThawsBeforeItFreezes()
    {
        var fixture = new Fixture(new TestPlayerQuery(0), transitionsPerFrame: 1);
        fixture.Resolver.SetReferencePosition(new Vector3Int(1019, 500, 0));
        var playerEntityId = fixture.Spawn(new Vector3Int(1019, 500, 0));
        var freezing = fixture.Spawn(new Vector3Int(500, 500, 0));
        var thawing = fixture.Spawn(new Vector3Int(1500, 500, 0));
        fixture.Frame();

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(thawing));
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(freezing), "Still simulated: its freeze is queued behind the thaw.");

        for (var i = 0; i < 200 && fixture.Resolver.Transitions.HasPending; i++)
        {
            fixture.Frame();
        }

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(freezing));
    }

    /// <summary>A change between two unsimulated tiers drains last: the neighborhood behind the player freezes before the one past it drops from Borough to Beyond.</summary>
    [TestMethod]
    public void PresetReference_Crossing_FreezesBeforeItMovesBetweenUnsimulatedTiers()
    {
        var fixture = new Fixture(new TestPlayerQuery(0), transitionsPerFrame: 1);
        fixture.Resolver.SetReferencePosition(new Vector3Int(1019, 500, 0));
        var playerEntityId = fixture.Spawn(new Vector3Int(1019, 500, 0));
        var falling = fixture.Spawn(new Vector3Int(-500, 500, 0));
        var freezing = fixture.Spawn(new Vector3Int(500, 500, 0));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(falling), "Precondition: neighborhood -1 is in the ring.");

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        for (var i = 0; i < 200 && fixture.TierOf(freezing) != ProcessingTierLevel.Borough; i++)
        {
            fixture.Frame();
        }

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(freezing));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(falling), "Still Borough: its drop to Beyond is queued behind the freeze.");

        for (var i = 0; i < 200 && fixture.Resolver.Transitions.HasPending; i++)
        {
            fixture.Frame();
        }

        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.TierOf(falling));
    }

    /// <summary>A neighborhood that isn't loaded when the window moves is never queued: whatever loads it creates its entities with the new window's tier.</summary>
    [TestMethod]
    public void PresetReference_Crossing_SkipsNeighborhoodsThatAreNotLoaded()
    {
        var fixture = new Fixture(new TestPlayerQuery(0), transitionsPerFrame: 1);
        fixture.Resolver.SetReferencePosition(new Vector3Int(1019, 500, 0));
        var playerEntityId = fixture.Spawn(new Vector3Int(1019, 500, 0));
        var inUnloadedNeighborhood = fixture.Spawn(new Vector3Int(-500, 500, 0));
        var inLoadedNeighborhood = fixture.Spawn(new Vector3Int(1500, 500, 0));
        fixture.Frame();
        fixture.Map.UnloadedNeighborhoods.Add((-1, 0));

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();
        for (var i = 0; i < 200 && fixture.Resolver.Transitions.HasPending; i++)
        {
            fixture.Frame();
        }

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(inLoadedNeighborhood));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(inUnloadedNeighborhood), "Never walked, so never recomputed.");
    }

    /// <summary>Crossing back before the queue drains cancels the pending work by making it a no-op -- the drain recomputes each tier when it reaches the entity, so nothing is applied from the abandoned crossing.</summary>
    [TestMethod]
    public void PresetReference_CrossesBackBeforeDraining_LeavesEveryTierCorrect()
    {
        var fixture = new Fixture(new TestPlayerQuery(0), transitionsPerFrame: 1);
        fixture.Resolver.SetReferencePosition(new Vector3Int(1019, 500, 0));
        var playerEntityId = fixture.Spawn(new Vector3Int(1019, 500, 0));
        var home = fixture.Spawn(new Vector3Int(500, 500, 0));
        var acrossTheBorder = fixture.Spawn(new Vector3Int(1500, 500, 0));
        fixture.Frame();

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();
        fixture.Move(playerEntityId, new Vector3Int(1019, 500, 0));

        for (var i = 0; i < 200 && fixture.Resolver.Transitions.HasPending; i++)
        {
            fixture.Frame();
        }

        Assert.IsFalse(fixture.Resolver.Transitions.HasPending);
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(home));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(acrossTheBorder));
    }

    /// <summary>Local is never queued: an entity the player walks up to is promoted the same frame, even while a crossing's transitions are still draining.</summary>
    [TestMethod]
    public void PresetReference_CrossingPromotesNearbyEntityToLocalImmediately()
    {
        var fixture = new Fixture(new TestPlayerQuery(0), transitionsPerFrame: 1);
        fixture.Resolver.SetReferencePosition(new Vector3Int(1019, 500, 0));
        var playerEntityId = fixture.Spawn(new Vector3Int(1019, 500, 0));
        var nearby = fixture.Spawn(new Vector3Int(1100, 500, 0));
        fixture.Frame();
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(nearby), "Precondition: across the border, outside Local.");

        fixture.Move(playerEntityId, new Vector3Int(1029, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(nearby));
        Assert.IsTrue(fixture.Resolver.Transitions.HasPending, "Precondition: the crossing's own transitions are still draining.");
    }

    /// <summary>The edge walk isn't clamped at 0: stepping toward an entity west of the origin promotes it.</summary>
    [TestMethod]
    public void PresetReference_PlayerStepsTowardAnEntityWestOfTheOrigin_IsPromotedToLocal()
    {
        var fixture = PresetFixture(new Vector3Int(10, 500, 0), out var playerEntityId);
        var otherEntityId = fixture.Spawn(new Vector3Int(-75, 500, 0));
        Assert.AreNotEqual(ProcessingTierLevel.Local, fixture.TierOf(otherEntityId), "Precondition: 85 tiles away.");

        fixture.Move(playerEntityId, new Vector3Int(0, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.TierOf(otherEntityId));
    }

    [TestMethod]
    public void PresetReference_PlayerCrossesZeroIntoNeighborhoodMinusOne_RetiersBothSides()
    {
        var fixture = PresetFixture(new Vector3Int(5, 500, 0), out var playerEntityId);
        var westEntityId = fixture.Spawn(new Vector3Int(-500, 500, 0));
        var eastEntityId = fixture.Spawn(new Vector3Int(500, 500, 0));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(westEntityId), "Precondition: neighborhood -1 is in the ring.");

        fixture.Move(playerEntityId, new Vector3Int(-5, 500, 0));
        fixture.Frame();

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(westEntityId));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(eastEntityId));
    }

    /// <summary>A fixture like PresetFixture with a window centred on neighborhood (0, 0), recording every shift.</summary>
    private static Fixture WindowFixture(Vector3Int playerPosition, out int playerEntityId, List<((int, int) Previous, (int, int) Center)> shifts)
    {
        var fixture = new Fixture(new TestPlayerQuery(0));
        fixture.Resolver.SetReferencePosition(playerPosition);
        fixture.Resolver.SetWindowCenter(0, 0);
        fixture.Resolver.WindowShifted += (previous, center) => shifts.Add((previous, center));
        playerEntityId = fixture.Spawn(playerPosition);
        fixture.Frame();
        return fixture;
    }

    [TestMethod]
    public void Window_PlayerJustAcrossTheBorder_NothingShiftsOrRetiers()
    {
        var shifts = new List<((int, int) Previous, (int, int) Center)>();
        var fixture = WindowFixture(new Vector3Int(1019, 500, 0), out var playerEntityId, shifts);
        var eastEntityId = fixture.Spawn(new Vector3Int(1600, 500, 0));
        var westEntityId = fixture.Spawn(new Vector3Int(400, 500, 0));

        fixture.Move(playerEntityId, new Vector3Int(1080, 500, 0));
        fixture.Frame();

        Assert.IsEmpty(shifts);
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(eastEntityId));
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(westEntityId));
    }

    [TestMethod]
    public void Window_PlayerSixtyFourTilesIn_ShiftsOnceAndRetiersBothNeighborhoods()
    {
        var shifts = new List<((int, int) Previous, (int, int) Center)>();
        var fixture = WindowFixture(new Vector3Int(1080, 500, 0), out var playerEntityId, shifts);
        var eastEntityId = fixture.Spawn(new Vector3Int(1600, 500, 0));
        var westEntityId = fixture.Spawn(new Vector3Int(400, 500, 0));

        fixture.Move(playerEntityId, new Vector3Int(1087, 500, 0));
        fixture.Frame();
        fixture.Move(playerEntityId, new Vector3Int(1088, 500, 0));
        fixture.Frame();

        CollectionAssert.AreEqual(new[] { ((0, 0), (1, 0)) }, shifts);
        Assert.AreEqual((1, 0), fixture.Resolver.WindowCenter);
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, fixture.TierOf(eastEntityId));
        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(westEntityId));
    }

    /// <summary>A creature born after the shift is tiered against the window centre too.</summary>
    [TestMethod]
    public void Window_EntityCreatedWhileThePlayerIsInsideTheGrace_IsBornAgainstTheCentre()
    {
        var shifts = new List<((int, int) Previous, (int, int) Center)>();
        var fixture = WindowFixture(new Vector3Int(1030, 500, 0), out _, shifts);

        var eastEntityId = fixture.Spawn(new Vector3Int(1900, 500, 0));

        Assert.AreEqual(ProcessingTierLevel.Borough, fixture.TierOf(eastEntityId));
    }
}
