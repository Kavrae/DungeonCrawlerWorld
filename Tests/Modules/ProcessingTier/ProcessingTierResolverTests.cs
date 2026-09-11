using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Tests.Modules.ProcessingTier;

[TestClass]
public sealed class ProcessingTierResolverTests
{
    private static readonly Vector3Int Reference = new(500, 500, 0);

    private sealed class Fixture
    {
        public ComponentManager ComponentManager { get; } = new(initialEntityCapacity: 10, initialComponentCapacity: 10);
        public EntityManager EntityManager { get; }
        public DirectComponentPool<TransformComponent> Transforms { get; } = new(10, static (ref existing, incoming) => existing = incoming);
        public DirectComponentPool<ProcessingTierComponent> Tiers { get; } = new(10, static (ref existing, incoming) => existing = incoming);
        public ProcessingTierEvents Events { get; } = new();
        public ProcessingTierResolver Resolver { get; } = new();
        public List<(int EntityId, ProcessingTierLevel Tier)> Raised { get; } = [];

        public Fixture(bool setReference = true)
        {
            EntityManager = new EntityManager(ComponentManager, 10);
            Resolver.Wire(Tiers, Transforms, Events);
            Events.TierChanged += (entityId, tier) => Raised.Add((entityId, tier));

            if (setReference)
            {
                Resolver.SetReferencePosition(Reference);
            }
        }
    }

    // --- CreateEntityAt: tier-first. -----------------------------------------------------

    [TestMethod]
    public void CreateEntityAt_WritesTierSilently()
    {
        var fixture = new Fixture();

        var entityId = fixture.Resolver.CreateEntityAt(fixture.EntityManager, new Vector3Int(510, 500, 0));

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.Tiers.GetReadonly(entityId).Tier);
        Assert.IsEmpty(fixture.Raised, "Tier-first is silent: nothing can hold a just-created entity, so there is nobody to tell.");
    }

    [TestMethod]
    public void CreateEntityAt_BeforeAnyReference_CreatesEntityUntiered()
    {
        var fixture = new Fixture(setReference: false);

        var entityId = fixture.Resolver.CreateEntityAt(fixture.EntityManager, new Vector3Int(510, 500, 0));

        Assert.IsTrue(fixture.EntityManager.EntityExists(entityId));
        Assert.IsFalse(fixture.Tiers.Has(entityId));
    }

    /// <summary>
    /// The mechanism the whole tier-first design rests on. A stripe set wired through
    /// ProcessingTierWiring reads an entity's tier in OnMemberAdded, so an entity born with its tier
    /// lands straight in the right bucket when the blueprint adds the driving component -- no
    /// TierChanged, no migration, and not the fail-open Beyond default.
    /// </summary>
    [TestMethod]
    public void CreateEntityAt_ThenJoiningATieredPool_LandsInTheCorrectBucketWithNoEvent()
    {
        var fixture = new Fixture();
        var movement = new PackedComponentPool<MovementComponent>(10, 10, static (ref existing, incoming) => existing = incoming);
        var stripeSet = ProcessingTierWiring.CreateAndWire(1, movement, fixture.Tiers, fixture.Events);

        var entityId = fixture.Resolver.CreateEntityAt(fixture.EntityManager, new Vector3Int(510, 500, 0));
        movement.Add(entityId, new MovementComponent(MovementMode.Random, null, null));

        // Base stripe count 1, so every tier's bucket stripe count is its divisor; bucket 0 is
        // due at frame 0 for every tier, and entity 0 lands in bucket 0 of whichever tier it is in.
        Assert.AreEqual(0, entityId, "Precondition: a fresh EntityManager hands out id 0.");
        CollectionAssert.Contains(stripeSet.GetTierBucket((int)ProcessingTierLevel.Local, 0).ToArray(), entityId);
        CollectionAssert.DoesNotContain(stripeSet.GetTierBucket((int)ProcessingTierLevel.Beyond, 0).ToArray(), entityId);
        Assert.IsEmpty(fixture.Raised);
    }

    // --- EnsureTiered: the after-placement catch-all. ------------------------------------

    /// <summary>An untiered entity is held by every consumer at the fail-open Beyond default, so landing on a *different* tier must be announced.</summary>
    [TestMethod]
    public void EnsureTiered_UntieredEntityLandsNonBeyond_RaisesTierChanged()
    {
        var fixture = new Fixture();

        fixture.Resolver.EnsureTiered(3, new Vector3Int(510, 500, 0));

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.Tiers.GetReadonly(3).Tier);
        CollectionAssert.AreEqual(new[] { (3, ProcessingTierLevel.Local) }, fixture.Raised);
    }

    /// <summary>...but landing on Beyond is what every consumer already assumed, so no event.</summary>
    [TestMethod]
    public void EnsureTiered_UntieredEntityLandsBeyond_NoEvent()
    {
        var fixture = new Fixture();

        fixture.Resolver.EnsureTiered(3, new Vector3Int(510, 500, 1));

        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.Tiers.GetReadonly(3).Tier);
        Assert.IsEmpty(fixture.Raised);
    }

    /// <summary>For an entity CreateEntityAt already tiered correctly, EnsureTiered is a read-and-compare -- the bulk population path must not pay an event per entity.</summary>
    [TestMethod]
    public void EnsureTiered_AfterCreateEntityAtAtTheSamePosition_NoEvent()
    {
        var fixture = new Fixture();
        var position = new Vector3Int(510, 500, 0);
        var entityId = fixture.Resolver.CreateEntityAt(fixture.EntityManager, position);

        fixture.Resolver.EnsureTiered(entityId, position);

        Assert.IsEmpty(fixture.Raised);
    }

    /// <summary>A blueprint that sets its own Z can land the entity somewhere other than planned. EnsureTiered corrects it, with the event every consumer needs.</summary>
    [TestMethod]
    public void EnsureTiered_PlacedSomewhereOtherThanPlanned_CorrectsAndRaises()
    {
        var fixture = new Fixture();
        var entityId = fixture.Resolver.CreateEntityAt(fixture.EntityManager, new Vector3Int(510, 500, 0));

        fixture.Resolver.EnsureTiered(entityId, new Vector3Int(510, 500, 1));

        Assert.AreEqual(ProcessingTierLevel.Beyond, fixture.Tiers.GetReadonly(entityId).Tier);
        CollectionAssert.AreEqual(new[] { (entityId, ProcessingTierLevel.Beyond) }, fixture.Raised);
    }

    // --- Pinning. ------------------------------------------------------------------------

    [TestMethod]
    public void PinnedEntity_IsNeverRetiered()
    {
        var fixture = new Fixture();
        fixture.Transforms.Add(4, new TransformComponent(new Vector3Int(5000, 5000, 2), new Vector2Byte(1, 1)));
        fixture.Resolver.PinLocalAndNotify(4);
        fixture.Raised.Clear();

        fixture.Resolver.Retier(4);
        fixture.Resolver.EnsureTiered(4, new Vector3Int(5000, 5000, 2));

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.Tiers.GetReadonly(4).Tier);
        Assert.IsEmpty(fixture.Raised);
    }

    /// <summary>The only public pin notifies, so pinning an entity that is already in tiered pools can never leave them holding it at Beyond.</summary>
    [TestMethod]
    public void PinLocalAndNotify_AnnouncesAChangeButNotANoOp()
    {
        var fixture = new Fixture();

        fixture.Resolver.PinLocalAndNotify(4);
        fixture.Resolver.PinLocalAndNotify(4);

        CollectionAssert.AreEqual(new[] { (4, ProcessingTierLevel.Local) }, fixture.Raised);
    }

    // --- ComputeTier. --------------------------------------------------------------------

    [TestMethod]
    public void ComputeTier_Hysteresis()
    {
        var at90 = new Vector3Int(590, 500, 0);

        Assert.AreEqual(ProcessingTierLevel.Neighborhood, ProcessingTierResolver.ComputeTier(at90, Reference, previousTier: null), "Entering needs <= 80.");
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, ProcessingTierResolver.ComputeTier(at90, Reference, ProcessingTierLevel.Neighborhood));
        Assert.AreEqual(ProcessingTierLevel.Local, ProcessingTierResolver.ComputeTier(at90, Reference, ProcessingTierLevel.Local), "Leaving needs > 96.");
        Assert.AreEqual(ProcessingTierLevel.Neighborhood, ProcessingTierResolver.ComputeTier(new Vector3Int(597, 500, 0), Reference, ProcessingTierLevel.Local));
    }

    [TestMethod]
    public void ComputeTier_DifferentLayerIsAlwaysBeyond() =>
        Assert.AreEqual(ProcessingTierLevel.Beyond, ProcessingTierResolver.ComputeTier(new Vector3Int(500, 500, 1), Reference, ProcessingTierLevel.Local));
}
