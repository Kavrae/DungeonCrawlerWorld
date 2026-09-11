using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Movement.Components;
using Game.Modules.ProcessingTier;
using Game.Modules.ProcessingTier.Components;

namespace Tests.Modules.ProcessingTier;

[TestClass]
public sealed class LocalTierRosterTests
{
    private sealed class Fixture
    {
        public PackedComponentPool<MovementComponent> Movers { get; } = new(10, 10, static (ref existing, incoming) => existing = incoming);
        public DirectComponentPool<ProcessingTierComponent> Tiers { get; } = new(10, static (ref existing, incoming) => existing = incoming);
        public DirectComponentPool<TransformComponent> Transforms { get; } = new(10, static (ref existing, incoming) => existing = incoming);
        public ProcessingTierEvents Events { get; } = new();
        public ProcessingTierResolver Resolver { get; } = new();
        public LocalTierRoster Roster { get; } = new();

        public Fixture()
        {
            Resolver.Wire(Tiers, Transforms, Events);
            Resolver.SetReferencePosition(new Vector3Int(500, 500, 0));
            Roster.Wire(Movers, Tiers, Events);
        }

        public void AddMover(int entityId) => Movers.Add(entityId, new MovementComponent(MovementMode.Random, null, null));
    }

    /// <summary>
    /// Under tier-first an entity is born with its tier and never raises TierChanged for it, so a
    /// mover born Local must be admitted when it gains MovementComponent. The roster used to rely on
    /// TierChanged alone, on the grounds that a new entity had no tier yet -- true of the old periodic
    /// scan, false now, and without this the enemy-telegraph consumer
    /// (ActionTargetingController.AllPendingDelayedActionTargets) would silently see nobody.
    /// </summary>
    [TestMethod]
    public void MoverBornLocal_IsAdmittedOnJoiningTheDrivingPool_WithNoTierChanged()
    {
        var fixture = new Fixture();
        fixture.Tiers.Add(3, new ProcessingTierComponent(ProcessingTierLevel.Local));

        fixture.AddMover(3);

        Assert.IsTrue(fixture.Roster.IsLocal(3));
    }

    [TestMethod]
    public void MoverBornNonLocal_IsNotAdmitted()
    {
        var fixture = new Fixture();
        fixture.Tiers.Add(3, new ProcessingTierComponent(ProcessingTierLevel.Neighborhood));

        fixture.AddMover(3);

        Assert.IsFalse(fixture.Roster.IsLocal(3));
    }

    /// <summary>Unknown is not Local -- an entity joining with no tier at all stays out.</summary>
    [TestMethod]
    public void MoverWithNoTier_IsNotAdmitted()
    {
        var fixture = new Fixture();

        fixture.AddMover(3);

        Assert.IsFalse(fixture.Roster.IsLocal(3));
    }

    /// <summary>
    /// Tiering now covers every positioned entity, so TierChanged fires for terrain too. The roster
    /// stays movers-only -- its value is being the small side, and the terrain within the Local
    /// radius alone would make it roughly fifty times larger.
    /// </summary>
    [TestMethod]
    public void NonMoverPromotedToLocal_IsNotAdmitted()
    {
        var fixture = new Fixture();
        fixture.Transforms.Add(3, new TransformComponent(new Vector3Int(510, 500, 0), new Vector2Byte(1, 1)));

        fixture.Resolver.EnsureTiered(3, new Vector3Int(510, 500, 0));

        Assert.AreEqual(ProcessingTierLevel.Local, fixture.Tiers.GetReadonly(3).Tier);
        Assert.IsFalse(fixture.Roster.IsLocal(3));
    }

    [TestMethod]
    public void MoverPromotedByTierChanged_IsAdmitted_AndDemotionRemovesIt()
    {
        var fixture = new Fixture();
        fixture.AddMover(3);
        fixture.Transforms.Add(3, new TransformComponent(new Vector3Int(510, 500, 0), new Vector2Byte(1, 1)));

        fixture.Resolver.Retier(3);
        Assert.IsTrue(fixture.Roster.IsLocal(3));

        fixture.Transforms.TrySet(3, new TransformComponent(new Vector3Int(700, 500, 0), new Vector2Byte(1, 1)));
        fixture.Resolver.Retier(3);
        Assert.IsFalse(fixture.Roster.IsLocal(3));
    }

    [TestMethod]
    public void MoverRemovedFromDrivingPool_LeavesTheRoster()
    {
        var fixture = new Fixture();
        fixture.Tiers.Add(3, new ProcessingTierComponent(ProcessingTierLevel.Local));
        fixture.AddMover(3);

        fixture.Movers.Remove(3);

        Assert.IsFalse(fixture.Roster.IsLocal(3));
    }
}
