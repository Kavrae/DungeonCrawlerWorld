using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Death.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Health.Systems;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Health;

[TestClass]
public sealed class DamageLedgerTests
{
    private const int VictimEntityId = 0;
    private const int FirstAttackerEntityId = 1;
    private const int SecondAttackerEntityId = 2;

    private sealed class LedgerFixture
    {
        public MultiComponentPool<DamageContributionComponent> Contributions { get; } = EmptyPools.Multi<DamageContributionComponent>();

        public PackedComponentPool<DamageLedgerExpiryComponent> Expiries { get; } = EmptyPools.Packed<DamageLedgerExpiryComponent>();

        public PackedComponentPool<DeadComponent> DeadEntities { get; } = EmptyPools.Packed<DeadComponent>();

        public PackedComponentPool<SimpleHealthComponent> Health { get; } = EmptyPools.Packed<SimpleHealthComponent>();

        public DamageLedger Ledger { get; }

        public DamageLedgerExpirySystem ExpirySystem { get; }

        public LedgerFixture()
        {
            var entityKeys = new EntityKeys();
            for (var entityId = 0; entityId <= SecondAttackerEntityId; entityId++)
            {
                entityKeys.Issue(entityId);
            }

            Ledger = new DamageLedger(Contributions, Expiries, DeadEntities, entityKeys);
            ExpirySystem = new DamageLedgerExpirySystem(Expiries, Ledger);
        }

        public float TotalFrom(int attackerEntityId) =>
            Contributions.TryGetFirst(VictimEntityId, TestSources.KeyOf(attackerEntityId),
                static (ref readonly DamageContributionComponent contribution, EntityKey key) => contribution.Source.Key == key, out var contribution)
                ? contribution.TotalDamageDealt
                : 0f;

        public void RunFrames(long fromFrame, long toFrameInclusive)
        {
            for (var frame = fromFrame; frame <= toFrameInclusive; frame++)
            {
                ExpirySystem.Update(new EngineTime(default, default, false, frame), 0);
            }
        }
    }

    [TestMethod]
    public void Record_KeepsASeparateTotalPerSource()
    {
        var fixture = new LedgerFixture();

        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(FirstAttackerEntityId), 5, now: 0);
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(SecondAttackerEntityId), 3, now: 1);
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(FirstAttackerEntityId), 4, now: 2);

        Assert.AreEqual(9f, fixture.TotalFrom(FirstAttackerEntityId));
        Assert.AreEqual(3f, fixture.TotalFrom(SecondAttackerEntityId));
        Assert.AreEqual(2, fixture.Contributions.CountForEntity(VictimEntityId));
    }

    [TestMethod]
    public void TryGetTopContributor_NamesTheHighestTotal()
    {
        var fixture = new LedgerFixture();
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(FirstAttackerEntityId), 5, now: 0);
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(SecondAttackerEntityId), 8, now: 1);

        Assert.IsTrue(fixture.Ledger.TryGetTopContributor(VictimEntityId, out var topContributorEntityKey));
        Assert.AreEqual(TestSources.KeyOf(SecondAttackerEntityId), topContributorEntityKey);
    }

    [TestMethod]
    public void TryGetTopContributor_ATieGoesToWhoeverHitFirst()
    {
        var fixture = new LedgerFixture();
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(SecondAttackerEntityId), 4, now: 10);
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(FirstAttackerEntityId), 6, now: 20);
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(SecondAttackerEntityId), 2, now: 30);

        Assert.IsTrue(fixture.Ledger.TryGetTopContributor(VictimEntityId, out var topContributorEntityKey));
        Assert.AreEqual(TestSources.KeyOf(SecondAttackerEntityId), topContributorEntityKey);
    }

    [TestMethod]
    public void TryGetTopContributor_NoContributions_NamesNobody()
    {
        var fixture = new LedgerFixture();

        Assert.IsFalse(fixture.Ledger.TryGetTopContributor(VictimEntityId, out var topContributorEntityKey));
        Assert.IsTrue(topContributorEntityKey.IsNone);
    }

    [TestMethod]
    public void Record_TerrainAdminAndSelfDamage_CreditNobody()
    {
        var fixture = new LedgerFixture();

        fixture.Ledger.Record(VictimEntityId, ActionSource.FromTerrain(1), 5, now: 0);
        fixture.Ledger.Record(VictimEntityId, ActionSource.Admin, 5, now: 0);
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(VictimEntityId), 5, now: 0);

        Assert.AreEqual(0, fixture.Contributions.CountForEntity(VictimEntityId));
        Assert.IsFalse(fixture.Expiries.Has(VictimEntityId));
    }

    [TestMethod]
    public void Record_DeadVictim_RecordsNothing()
    {
        var fixture = new LedgerFixture();
        fixture.DeadEntities.Add(VictimEntityId, new DeadComponent(ActionSource.Admin, DiedAtFrame: 0));

        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(FirstAttackerEntityId), 5, now: 0);

        Assert.AreEqual(0, fixture.Contributions.CountForEntity(VictimEntityId));
    }

    [TestMethod]
    public void SimpleDamage_RecordsOnlyTheHealthTheVictimHadLeft()
    {
        var fixture = new LedgerFixture();
        fixture.Health.Add(VictimEntityId, new SimpleHealthComponent(currentHealth: 10, maximumHealth: 100));

        TestHealth.Damage(fixture.Health, new EventBus(), VictimEntityId, 50, TestSources.Entity(FirstAttackerEntityId), null, "Test", now: 0,
            deadEntities: fixture.DeadEntities, damageLedger: fixture.Ledger);

        Assert.AreEqual(10f, fixture.TotalFrom(FirstAttackerEntityId));
    }

    [TestMethod]
    public void ComplexDamage_OnePart_RecordsOnlyWhatThatPartHadLeft()
    {
        var fixture = new LedgerFixture();
        var bodyParts = BodyPartTestWorld.WithParts(VictimEntityId, ("Head", BodyPartType.Head, 4, 30, true)).BodyParts;

        ComplexHealthDamage.Apply(fixture.Health, bodyParts, new EventBus(), VictimEntityId, 20, TestSources.Entity(FirstAttackerEntityId), TestPlayerQuery.NoPlayer, "Test",
            EmptyPools.Multi<StatModifierComponent>(), new MathUtility(), fixture.DeadEntities, fixture.Ledger, now: 0);

        Assert.AreEqual(4f, fixture.TotalFrom(FirstAttackerEntityId));
    }

    [TestMethod]
    public void ComplexDamage_AllParts_RecordsTheSumEachPartLost()
    {
        var fixture = new LedgerFixture();
        var bodyParts = BodyPartTestWorld.WithParts(VictimEntityId, ("Head", BodyPartType.Head, 3, 30, true), ("Torso", BodyPartType.Torso, 60, 60, true)).BodyParts;

        ComplexHealthDamage.ApplyToAllParts(fixture.Health, bodyParts, new EventBus(), VictimEntityId, 20, TestSources.Entity(FirstAttackerEntityId), TestPlayerQuery.NoPlayer, "Test",
            EmptyPools.Multi<StatModifierComponent>(), fixture.DeadEntities, fixture.Ledger, now: 0);

        Assert.AreEqual(13f, fixture.TotalFrom(FirstAttackerEntityId));
    }

    [TestMethod]
    public void PoisonTick_CreditsWhoeverAppliedThePoison()
    {
        var fixture = new LedgerFixture();
        fixture.Health.Add(VictimEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));
        var timers = EmptyPools.Packed<PoisonTimerComponent>();
        timers.Add(VictimEntityId, new PoisonTimerComponent(nextTickFrame: 1, stackCount: 3, remainingDurationTicks: 5, TestSources.Entity(SecondAttackerEntityId)));
        var system = TestSystems.PoisonSystem(timers, fixture.Health, new EventBus(), TestPlayerQuery.NoPlayer, new MathUtility(), damageLedger: fixture.Ledger);

        system.Update(new EngineTime(default, default, false, 1), 0);

        Assert.AreEqual(3f, fixture.TotalFrom(SecondAttackerEntityId));
    }

    [TestMethod]
    public void ExpirySystem_AfterThirtyQuietSeconds_ClearsTheLedger()
    {
        var fixture = new LedgerFixture();
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(FirstAttackerEntityId), 5, now: 0);

        fixture.RunFrames(0, DamageLedger.ResetAfterFrames - 1);
        Assert.AreEqual(1, fixture.Contributions.CountForEntity(VictimEntityId));

        fixture.RunFrames(DamageLedger.ResetAfterFrames, DamageLedger.ResetAfterFrames);
        Assert.AreEqual(0, fixture.Contributions.CountForEntity(VictimEntityId));
        Assert.IsFalse(fixture.Expiries.Has(VictimEntityId));
    }

    [TestMethod]
    public void ExpirySystem_AnyHitBeforeTheDeadline_KeepsEveryContributor()
    {
        var fixture = new LedgerFixture();
        const int LateHitFrame = DamageLedger.ResetAfterFrames - 60;
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(FirstAttackerEntityId), 5, now: 0);
        fixture.RunFrames(0, LateHitFrame - 1);

        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(SecondAttackerEntityId), 2, now: LateHitFrame);
        fixture.RunFrames(LateHitFrame, LateHitFrame + DamageLedger.ResetAfterFrames - 1);

        Assert.AreEqual(5f, fixture.TotalFrom(FirstAttackerEntityId));
        Assert.AreEqual(2f, fixture.TotalFrom(SecondAttackerEntityId));

        fixture.RunFrames(LateHitFrame + DamageLedger.ResetAfterFrames, LateHitFrame + DamageLedger.ResetAfterFrames);
        Assert.AreEqual(0, fixture.Contributions.CountForEntity(VictimEntityId));
    }

    [TestMethod]
    public void Record_AfterTheQuietPeriodButBeforeTheExpiryRan_StartsAFreshLedger()
    {
        var fixture = new LedgerFixture();
        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(FirstAttackerEntityId), 5, now: 0);

        fixture.Ledger.Record(VictimEntityId, TestSources.Entity(SecondAttackerEntityId), 2, now: DamageLedger.ResetAfterFrames);

        Assert.AreEqual(0f, fixture.TotalFrom(FirstAttackerEntityId));
        Assert.AreEqual(2f, fixture.TotalFrom(SecondAttackerEntityId));
    }
}
