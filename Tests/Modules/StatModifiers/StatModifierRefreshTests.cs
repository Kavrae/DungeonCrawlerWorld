using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Engine.Events;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatModifiers.Systems;
using Game.Tags;
using Game.World;

namespace Tests.Modules.StatModifiers;

[TestClass]
public sealed class StatModifierRefreshTests
{
    private const int EntityId = 0;
    private const ushort HolyGroundTerrainTypeId = 7;

    private static readonly ActionSource Terrain = ActionSource.FromTerrain(HolyGroundTerrainTypeId);

    private static MultiComponentPool<StatModifierComponent> CreatePool() => new(entityCapacity: 10, initialCapacity: 4);

    private static void Grant(MultiComponentPool<StatModifierComponent> pool, uint expiresAtFrame, ActionSource? source = null, float magnitude = -0.1f, Engine.Tags.GameplayTag conditionTag = default) =>
        StatModifierEffects.ApplyOrRefresh(pool, EntityId, StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: true, magnitude, expiresAtFrame, source ?? Terrain, conditionTag);

    private static StatModifierComponent Only(MultiComponentPool<StatModifierComponent> pool) =>
        pool.GetReadonlyByDenseIndex(pool.GetFirstDenseIndex(EntityId));

    [TestMethod]
    public void ApplyOrRefresh_NothingHeld_AddsTheModifier()
    {
        var pool = CreatePool();

        Grant(pool, expiresAtFrame: 300);

        Assert.AreEqual(1, pool.CountForEntity(EntityId));
        Assert.AreEqual(300u, Only(pool).ExpiresAtFrame);
        Assert.AreEqual(Terrain, Only(pool).Source);
    }

    [TestMethod]
    public void ApplyOrRefresh_SameModifierFromTheSameSource_MovesItsExpiryInsteadOfAddingASecond()
    {
        var pool = CreatePool();
        Grant(pool, expiresAtFrame: 300);

        Grant(pool, expiresAtFrame: 360);

        Assert.AreEqual(1, pool.CountForEntity(EntityId));
        Assert.AreEqual(360u, Only(pool).ExpiresAtFrame);
    }

    [TestMethod]
    public void ApplyOrRefresh_SameModifierFromADifferentSource_AddsASecond()
    {
        var pool = CreatePool();
        Grant(pool, expiresAtFrame: 300);

        Grant(pool, expiresAtFrame: 360, source: ActionSource.FromTerrain(HolyGroundTerrainTypeId + 1));

        Assert.AreEqual(2, pool.CountForEntity(EntityId));
    }

    [TestMethod]
    public void ApplyOrRefresh_DifferentMagnitudeOrCondition_AddsRatherThanRefreshing()
    {
        var pool = CreatePool();
        Grant(pool, expiresAtFrame: 300);

        Grant(pool, expiresAtFrame: 360, magnitude: -0.2f);
        Grant(pool, expiresAtFrame: 360, conditionTag: GameTags.DamageFire);

        Assert.AreEqual(3, pool.CountForEntity(EntityId));
    }

    /// <summary>The entity's expiry timer was scheduled for the first deadline. A refresh must leave the modifier standing when that frame comes, and it must still expire on the deadline the refresh gave it.</summary>
    [TestMethod]
    public void RefreshedModifier_OutlivesItsOriginalDeadlineAndExpiresOnTheNewOne()
    {
        var pool = CreatePool();
        var expiries = new PackedComponentPool<ExpiringStatModifierComponent>(entityCapacity: 10, initialCapacity: 4, static (ref existing, incoming) =>
            existing.NextTickFrame = System.Math.Min(existing.NextTickFrame, incoming.NextTickFrame));
        var system = new StatModifierExpirySystem(pool, expiries, new EventBus());
        Grant(pool, expiresAtFrame: 100);
        for (var frame = 1; frame <= 50; frame++)
        {
            system.Update(new EngineTime(default, default, false, frame), 0);
        }

        Grant(pool, expiresAtFrame: 150);
        for (var frame = 51; frame <= 149; frame++)
        {
            system.Update(new EngineTime(default, default, false, frame), 0);
        }

        Assert.AreEqual(1, pool.CountForEntity(EntityId), "Still held past the original deadline of 100.");

        system.Update(new EngineTime(default, default, false, 150), 0);

        Assert.AreEqual(0, pool.CountForEntity(EntityId));
    }
}
