using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Game.Modules.Death;
using Game.Modules.Death.Components;
using Game.World;

namespace Tests.Modules.Death;

[TestClass]
public sealed class LootRightsTests
{
    private const int CorpseEntityId = 0;
    private const int OwnerEntityId = 1;
    private const int OtherLooterEntityId = 2;
    private const long DiedAtFrame = 100;

    private sealed class RightsFixture
    {
        public PackedComponentPool<DeadComponent> DeadEntities { get; } = EmptyPools.Packed<DeadComponent>();

        public EntityKeys EntityKeys { get; } = new();

        public LootRights Rights { get; }

        public RightsFixture()
        {
            for (var entityId = 0; entityId <= OtherLooterEntityId; entityId++)
            {
                EntityKeys.Issue(entityId);
            }

            Rights = new LootRights(DeadEntities, EntityKeys);
        }

        public void KillCorpse(EntityKey lootOwnerEntityKey) =>
            DeadEntities.Add(CorpseEntityId, new DeadComponent(ActionSource.Admin, DiedAtFrame, lootOwnerEntityKey));
    }

    [TestMethod]
    public void CanLoot_DuringTheWindow_OnlyTheOwner()
    {
        var fixture = new RightsFixture();
        fixture.KillCorpse(TestSources.KeyOf(OwnerEntityId));

        Assert.IsTrue(fixture.Rights.CanLoot(CorpseEntityId, TestSources.KeyOf(OwnerEntityId), DiedAtFrame));
        Assert.IsFalse(fixture.Rights.CanLoot(CorpseEntityId, TestSources.KeyOf(OtherLooterEntityId), DiedAtFrame + LootRights.ExclusiveLootFrames - 1));
    }

    [TestMethod]
    public void CanLoot_OnceTheWindowEnds_Anyone()
    {
        var fixture = new RightsFixture();
        fixture.KillCorpse(TestSources.KeyOf(OwnerEntityId));

        Assert.IsTrue(fixture.Rights.CanLoot(CorpseEntityId, TestSources.KeyOf(OtherLooterEntityId), DiedAtFrame + LootRights.ExclusiveLootFrames));
    }

    [TestMethod]
    public void CanLoot_NoOwner_Anyone()
    {
        var fixture = new RightsFixture();
        fixture.KillCorpse(EntityKey.None);

        Assert.IsTrue(fixture.Rights.CanLoot(CorpseEntityId, TestSources.KeyOf(OtherLooterEntityId), DiedAtFrame));
    }

    [TestMethod]
    public void CanLoot_OwnerNoLongerLoaded_Anyone()
    {
        var fixture = new RightsFixture();
        fixture.KillCorpse(TestSources.KeyOf(OwnerEntityId));
        fixture.EntityKeys.Release(OwnerEntityId);

        Assert.IsTrue(fixture.Rights.CanLoot(CorpseEntityId, TestSources.KeyOf(OtherLooterEntityId), DiedAtFrame));
    }

    [TestMethod]
    public void CanLoot_OwnerIsDead_Anyone()
    {
        var fixture = new RightsFixture();
        fixture.KillCorpse(TestSources.KeyOf(OwnerEntityId));
        fixture.DeadEntities.Add(OwnerEntityId, new DeadComponent(ActionSource.Admin, DiedAtFrame));

        Assert.IsTrue(fixture.Rights.CanLoot(CorpseEntityId, TestSources.KeyOf(OtherLooterEntityId), DiedAtFrame));
    }

    [TestMethod]
    public void CanLoot_NotACorpse_Anyone()
    {
        var fixture = new RightsFixture();

        Assert.IsTrue(fixture.Rights.CanLoot(CorpseEntityId, TestSources.KeyOf(OtherLooterEntityId), DiedAtFrame));
    }
}
