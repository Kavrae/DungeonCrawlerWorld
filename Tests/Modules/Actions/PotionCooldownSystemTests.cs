using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Systems;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class PotionCooldownSystemTests
{
    private const int EntityId = 1;

    private static (PotionCooldownSystem System, PackedComponentPool<PotionCooldownComponent> Cooldowns) Build()
    {
        var cooldowns = new PackedComponentPool<PotionCooldownComponent>(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);
        return (new PotionCooldownSystem(cooldowns), cooldowns);
    }

    [TestMethod]
    public void Update_TicksFramesRemainingDownByOne()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 1200));

        system.Update(default, 0);

        Assert.AreEqual(1199, cooldowns.GetReadonly(EntityId).FramesRemaining);
    }

    [TestMethod]
    public void Update_FramesRemainingReachesZero_RemovesTheComponent()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(EntityId, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 1));

        system.Update(default, 0);

        Assert.IsFalse(cooldowns.Has(EntityId));
    }

    [TestMethod]
    public void Update_NoEntitiesWithCooldown_DoesNotThrow()
    {
        var (system, _) = Build();

        system.Update(default, 0);
    }

    [TestMethod]
    public void Update_MultipleEntities_EachTickedIndependently()
    {
        var (system, cooldowns) = Build();
        cooldowns.Add(1, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 500));
        cooldowns.Add(2, new PotionCooldownComponent(totalFrames: 1200, framesRemaining: 1));

        system.Update(default, 0);

        Assert.AreEqual(499, cooldowns.GetReadonly(1).FramesRemaining);
        Assert.IsFalse(cooldowns.Has(2));
    }
}
