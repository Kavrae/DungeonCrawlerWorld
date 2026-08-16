using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;
using Game.Modules.ProcessingTier.Systems;
using Game.World;

namespace Tests.Modules.ProcessingTier;

[TestClass]
public sealed class ProcessingTierSystemTests
{
    private const int PlayerEntityId = 1;
    private const int OtherEntityId = 0;

    private sealed class FakePlayerQuery(int playerEntityId) : IPlayerQuery
    {
        public int PlayerEntityId { get; } = playerEntityId;
    }

    private static DirectComponentPool<TransformComponent> CreateTransformPool(int capacity = 10) =>
        new(capacity, static (ref existing, incoming) => existing = incoming);

    private static PackedComponentPool<MovementComponent> CreateMovementPool(int capacity = 10) =>
        new(capacity, capacity, static (ref existing, incoming) => existing = incoming);

    private static DirectComponentPool<ProcessingTierComponent> CreateTiersPool(int capacity = 10) =>
        new(capacity, static (ref existing, incoming) => existing = incoming);

    private static (Game.Modules.ProcessingTier.Systems.ProcessingTierSystem System, DirectComponentPool<TransformComponent> Transforms, DirectComponentPool<ProcessingTierComponent> Tiers, ProcessingTierEvents Events) CreateSystem(Vector3Int otherPosition, Vector3Int playerPosition)
    {
        var transforms = CreateTransformPool();
        var movementComponents = CreateMovementPool();
        var tiers = CreateTiersPool();
        var events = new ProcessingTierEvents();

        transforms.Add(OtherEntityId, new TransformComponent(otherPosition, new Vector2Byte(1, 1)));
        transforms.Add(PlayerEntityId, new TransformComponent(playerPosition, new Vector2Byte(1, 1)));
        movementComponents.Add(OtherEntityId, new MovementComponent(MovementMode.Random, null, null));

        var system = new Game.Modules.ProcessingTier.Systems.ProcessingTierSystem(transforms, movementComponents, tiers, new FakePlayerQuery(PlayerEntityId), events);
        return (system, transforms, tiers, events);
    }

    [TestMethod]
    public void Update_NoPlayerQuery_LeavesEntityUntiered()
    {
        var transforms = CreateTransformPool();
        var movementComponents = CreateMovementPool();
        var tiers = CreateTiersPool();
        transforms.Add(OtherEntityId, new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1)));
        movementComponents.Add(OtherEntityId, new MovementComponent(MovementMode.Random, null, null));

        var system = new Game.Modules.ProcessingTier.Systems.ProcessingTierSystem(transforms, movementComponents, tiers, playerQuery: null, new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.IsFalse(tiers.Has(OtherEntityId));
    }

    [TestMethod]
    public void Update_PlayerQuerySetButNoPlayerTransformYet_LeavesEntityUntiered()
    {
        var transforms = CreateTransformPool();
        var movementComponents = CreateMovementPool();
        var tiers = CreateTiersPool();
        transforms.Add(OtherEntityId, new TransformComponent(new Vector3Int(2, 2, 0), new Vector2Byte(1, 1)));
        movementComponents.Add(OtherEntityId, new MovementComponent(MovementMode.Random, null, null));
        // Player entity has no TransformComponent yet -- pre-spawn.

        var system = new Game.Modules.ProcessingTier.Systems.ProcessingTierSystem(transforms, movementComponents, tiers, new FakePlayerQuery(PlayerEntityId), new ProcessingTierEvents());
        system.Update(default, 0);

        Assert.IsFalse(tiers.Has(OtherEntityId));
    }

    [TestMethod]
    public void Update_WithinLocalRadius_GetsLocalTier()
    {
        var (system, _, tiers, _) = CreateSystem(otherPosition: new Vector3Int(2, 2, 0), playerPosition: new Vector3Int(2, 2, 0));

        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Local, tiers.GetReadonly(OtherEntityId).Tier);
    }

    [TestMethod]
    public void Update_OutsideLocalButSameNeighborhoodCell_GetsNeighborhoodTier()
    {
        // Player at (500,500) (neighborhood cell (0,0)); other at (700,500) -- Chebyshev distance
        // 200, well outside LocalRadiusTiles (80), but floor(700/1000) == floor(500/1000) == 0,
        // the same neighborhood cell as the player.
        var (system, _, tiers, _) = CreateSystem(otherPosition: new Vector3Int(700, 500, 0), playerPosition: new Vector3Int(500, 500, 0));

        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, tiers.GetReadonly(OtherEntityId).Tier);
    }

    [TestMethod]
    public void Update_SameBoroughButDifferentNeighborhood_GetsBoroughTier()
    {
        // Player at (900,500) (neighborhood (0,0), borough (0,0)); other at (1100,500)
        // (neighborhood (1,0), borough floor(1100/2000)=0 -- same borough, different
        // neighborhood). Distance 200, outside Local.
        var (system, _, tiers, _) = CreateSystem(otherPosition: new Vector3Int(1100, 500, 0), playerPosition: new Vector3Int(900, 500, 0));

        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Borough, tiers.GetReadonly(OtherEntityId).Tier);
    }

    [TestMethod]
    public void Update_DifferentMapLayer_GetsBeyondTier_RegardlessOfXY()
    {
        // Same X/Y as the player -- would be Local by distance alone -- but a different Z
        // (MapLayer) is never visible to the player, so it must still be Beyond.
        var (system, _, tiers, _) = CreateSystem(otherPosition: new Vector3Int(2, 2, 1), playerPosition: new Vector3Int(2, 2, 0));

        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Beyond, tiers.GetReadonly(OtherEntityId).Tier);
    }

    [TestMethod]
    public void Update_FarBeyondAnyRegion_GetsBeyondTier()
    {
        var (system, _, tiers, _) = CreateSystem(otherPosition: new Vector3Int(5000, 5000, 0), playerPosition: new Vector3Int(0, 0, 0));

        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Beyond, tiers.GetReadonly(OtherEntityId).Tier);
    }

    /// <summary>Hysteresis: an entity already Local (distance 0 on the first visit) must stay Local on a later visit at distance 90 -- past LocalRadiusTiles (80) but within the wider exit boundary (80 + 16 = 96) -- instead of immediately dropping to Neighborhood the moment it crosses the entry radius.</summary>
    [TestMethod]
    public void Update_AlreadyLocal_StaysLocalWithinExitBuffer_EvenPastEntryRadius()
    {
        var (system, transforms, tiers, _) = CreateSystem(otherPosition: new Vector3Int(0, 0, 0), playerPosition: new Vector3Int(0, 0, 0));
        system.Update(default, 0);
        Assert.AreEqual(ProcessingTierLevel.Local, tiers.GetReadonly(OtherEntityId).Tier);

        transforms.TrySet(PlayerEntityId, new TransformComponent(new Vector3Int(90, 0, 0), new Vector2Byte(1, 1)));
        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Local, tiers.GetReadonly(OtherEntityId).Tier);
    }

    /// <summary>Complements the test above: once distance exceeds the wider exit boundary (96), the entity finally leaves Local.</summary>
    [TestMethod]
    public void Update_AlreadyLocal_ExitsOncePastExitBuffer()
    {
        var (system, transforms, tiers, _) = CreateSystem(otherPosition: new Vector3Int(0, 0, 0), playerPosition: new Vector3Int(0, 0, 0));
        system.Update(default, 0);
        Assert.AreEqual(ProcessingTierLevel.Local, tiers.GetReadonly(OtherEntityId).Tier);

        transforms.TrySet(PlayerEntityId, new TransformComponent(new Vector3Int(100, 0, 0), new Vector2Byte(1, 1)));
        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, tiers.GetReadonly(OtherEntityId).Tier);
    }

    /// <summary>A fresh entry (never previously Local) at distance 90 must NOT get Local's hysteresis benefit -- only an entity already Local when last visited gets the wider exit radius; a brand new entity at the same distance uses the plain entry radius (80) and lands in Neighborhood.</summary>
    [TestMethod]
    public void Update_NeverLocalBefore_AtDistanceWithinExitBufferOnly_DoesNotGetLocalTier()
    {
        var (system, _, tiers, _) = CreateSystem(otherPosition: new Vector3Int(90, 0, 0), playerPosition: new Vector3Int(0, 0, 0));

        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, tiers.GetReadonly(OtherEntityId).Tier);
    }

    [TestMethod]
    public void Update_FirstComputation_RaisesTierChangedOnce()
    {
        var (system, _, _, events) = CreateSystem(otherPosition: new Vector3Int(2, 2, 0), playerPosition: new Vector3Int(2, 2, 0));
        var raisedCount = 0;
        ProcessingTierLevel? raisedTier = null;
        events.TierChanged += (entityId, tier) =>
        {
            Assert.AreEqual(OtherEntityId, entityId);
            raisedTier = tier;
            raisedCount++;
        };

        system.Update(default, 0);

        Assert.AreEqual(1, raisedCount);
        Assert.AreEqual(ProcessingTierLevel.Local, raisedTier);
    }

    [TestMethod]
    public void Update_RecomputeToSameTier_DoesNotRaiseTierChangedAgain()
    {
        var (system, _, _, events) = CreateSystem(otherPosition: new Vector3Int(2, 2, 0), playerPosition: new Vector3Int(2, 2, 0));
        system.Update(default, 0);

        var raisedCount = 0;
        events.TierChanged += (_, _) => raisedCount++;
        system.Update(default, 0);

        Assert.AreEqual(0, raisedCount);
    }

    [TestMethod]
    public void Update_RecomputeToDifferentTier_RaisesTierChangedWithNewTier()
    {
        var (system, transforms, _, events) = CreateSystem(otherPosition: new Vector3Int(0, 0, 0), playerPosition: new Vector3Int(0, 0, 0));
        system.Update(default, 0);

        ProcessingTierLevel? raisedTier = null;
        events.TierChanged += (_, tier) => raisedTier = tier;
        transforms.TrySet(PlayerEntityId, new TransformComponent(new Vector3Int(100, 0, 0), new Vector2Byte(1, 1)));
        system.Update(default, 0);

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, raisedTier);
    }
}
