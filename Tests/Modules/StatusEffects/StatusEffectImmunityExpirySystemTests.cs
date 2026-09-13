using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.Modules.StatusEffects.Systems;

namespace Tests.Modules.StatusEffects;

[TestClass]
public sealed class StatusEffectImmunityExpirySystemTests
{
    private const int EntityId = 1;

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    private static MultiComponentPool<StatusEffectImmunityComponent> CreatePool() => new(maximumEntityCount: 10, initialCapacity: 4);

    private static void Run(StatusEffectImmunityExpirySystem system, long from, long to)
    {
        for (var frame = from; frame <= to; frame++)
        {
            system.Update(Frame(frame), 0);
        }
    }

    private static bool HasImmunity(MultiComponentPool<StatusEffectImmunityComponent> immunities, StatusEffectType effectType)
    {
        for (var denseIndex = immunities.GetFirstDenseIndex(EntityId); denseIndex != -1; denseIndex = immunities.GetNextDenseIndex(denseIndex))
        {
            if (immunities.GetReadonlyByDenseIndex(denseIndex).EffectType == effectType)
            {
                return true;
            }
        }

        return false;
    }

    [TestMethod]
    public void BeforeItsExpiryFrame_ImmunityStays()
    {
        var immunities = CreatePool();
        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Burning, expiresAtFrame: 100);
        var system = new StatusEffectImmunityExpirySystem(immunities);

        Run(system, 0, 99);

        Assert.IsTrue(HasImmunity(immunities, StatusEffectType.Burning));
    }

    [TestMethod]
    public void OnItsExpiryFrame_ImmunityIsRemoved()
    {
        var immunities = CreatePool();
        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Burning, expiresAtFrame: 100);
        var system = new StatusEffectImmunityExpirySystem(immunities);

        Run(system, 0, 100);

        Assert.IsFalse(HasImmunity(immunities, StatusEffectType.Burning));
    }

    [TestMethod]
    public void PermanentImmunity_IsNeverRemoved()
    {
        var immunities = CreatePool();
        StatusEffectImmunityEffects.GrantPermanent(immunities, EntityId, StatusEffectType.Paralysis);
        var system = new StatusEffectImmunityExpirySystem(immunities);

        Run(system, 0, 500);

        Assert.IsTrue(HasImmunity(immunities, StatusEffectType.Paralysis));
    }

    /// <summary>Each type is its own wheel entry (keyed by EffectType), so one expiring never takes another with it.</summary>
    [TestMethod]
    public void TwoTypes_EachExpiresOnItsOwnFrame()
    {
        var immunities = CreatePool();
        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Burning, expiresAtFrame: 30);
        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Poison, expiresAtFrame: 90);
        var system = new StatusEffectImmunityExpirySystem(immunities);

        Run(system, 0, 30);
        Assert.IsFalse(HasImmunity(immunities, StatusEffectType.Burning));
        Assert.IsTrue(HasImmunity(immunities, StatusEffectType.Poison));

        Run(system, 31, 90);
        Assert.IsFalse(HasImmunity(immunities, StatusEffectType.Poison));
    }

    /// <summary>Re-granting a type the entity is already immune to extends the one instance instead of adding a second -- what keeps the wheel's (entity, EffectType) key unique.</summary>
    [TestMethod]
    public void ReGrantingTheSameType_ExtendsTheOneInstanceToTheLaterFrame()
    {
        var immunities = CreatePool();
        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Burning, expiresAtFrame: 50);
        var system = new StatusEffectImmunityExpirySystem(immunities);

        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Burning, expiresAtFrame: 200);
        Assert.AreEqual(1, immunities.CountForEntity(EntityId));

        Run(system, 0, 50);
        Assert.IsTrue(HasImmunity(immunities, StatusEffectType.Burning), "The old frame passed, but the immunity was extended.");

        Run(system, 51, 200);
        Assert.IsFalse(HasImmunity(immunities, StatusEffectType.Burning));
    }

    /// <summary>A shorter re-grant must not cut an immunity short either -- the later deadline always wins, and a permanent one is never shortened.</summary>
    [TestMethod]
    public void ReGrantingShorter_KeepsTheLongerDeadline()
    {
        var immunities = CreatePool();
        StatusEffectImmunityEffects.GrantPermanent(immunities, EntityId, StatusEffectType.Poison);
        var system = new StatusEffectImmunityExpirySystem(immunities);

        StatusEffectImmunityEffects.Grant(immunities, EntityId, StatusEffectType.Poison, expiresAtFrame: 10);

        Run(system, 0, 100);

        Assert.IsTrue(HasImmunity(immunities, StatusEffectType.Poison));
    }

    [TestMethod]
    public void NoImmunities_DoesNotThrow()
    {
        var system = new StatusEffectImmunityExpirySystem(CreatePool());

        system.Update(Frame(1), 0);
    }
}
