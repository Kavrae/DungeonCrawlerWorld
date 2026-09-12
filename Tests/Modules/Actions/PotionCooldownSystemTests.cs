using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Systems;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class PotionCooldownSystemTests
{
    private const int EntityId = 1;

    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    private static (PotionCooldownSystem System, PackedComponentPool<PotionCooldownComponent> Cooldowns) Build()
    {
        var cooldowns = new PackedComponentPool<PotionCooldownComponent>(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);
        return (new PotionCooldownSystem(cooldowns), cooldowns);
    }

    [TestMethod]
    public void BeforeItsExpiryFrame_CooldownStays()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 1200));

        system.Update(Frame(1199), 0);

        Assert.IsTrue(cooldowns.Has(EntityId));
        Assert.AreEqual(1, PotionCooldownEffects.FramesRemaining(cooldowns.GetReadonly(EntityId), now: 1199));
    }

    [TestMethod]
    public void OnItsExpiryFrame_CooldownIsRemoved()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 1200));

        system.Update(Frame(1200), 0);

        Assert.IsFalse(cooldowns.Has(EntityId));
    }

    [TestMethod]
    public void NoEntitiesWithCooldown_DoesNotThrow()
    {
        var (system, _) = Build();

        system.Update(default, 0);
    }

    /// <summary>Each cooldown ends on its own frame, independent of any other -- no stripe buckets, no shared cadence.</summary>
    [TestMethod]
    public void MultipleEntities_EachEndsOnItsOwnFrame()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(1, new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 500));
        cooldowns.Add(2, new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 2));

        system.Update(Frame(2), 0);

        Assert.IsTrue(cooldowns.Has(1));
        Assert.IsFalse(cooldowns.Has(2));

        system.Update(Frame(500), 0);

        Assert.IsFalse(cooldowns.Has(1));
    }

    /// <summary>Drinking again resets the cooldown through a Merge -- the system follows the new frame on its own, and the superseded one doesn't remove the fresh cooldown early.</summary>
    [TestMethod]
    public void ResetMidway_EndsAtTheNewFrameOnly()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 300));
        system.Update(Frame(100), 0);

        cooldowns.Merge(EntityId, new PotionCooldownComponent(totalFrames: 1200, expiresAtFrame: 1300));
        system.Update(Frame(300), 0);
        Assert.IsTrue(cooldowns.Has(EntityId));

        system.Update(Frame(1300), 0);
        Assert.IsFalse(cooldowns.Has(EntityId));
    }
}
