using Engine.ECS.Components.Stores;
using Game.Modules.Paralysis.Components;
using Game.Modules.Paralysis.Systems;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Paralysis;

[TestClass]
public sealed class ParalysisSystemTests
{
    private static PackedComponentPool<ParalysisTimerComponent> CreateTimerPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => { });

    private static MultiComponentPool<StatusEffectStack> CreateStackPool() => new(maximumEntityCount: 10, initialCapacity: 10);

    [TestMethod]
    public void Update_CountdownDecrementsByOnePerCall()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        timers.Add(0, new ParalysisTimerComponent(60));
        var system = new ParalysisSystem(timers, stacks);

        system.Update(default, 0);

        Assert.AreEqual(59, timers.GetReadonly(0).FramesUntilNextTick);
    }

    [TestMethod]
    public void Update_AtExpiryFrame_RemovesTimer()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        timers.Add(0, new ParalysisTimerComponent(1));
        var system = new ParalysisSystem(timers, stacks);

        system.Update(default, 0);

        Assert.IsFalse(timers.Has(0));
    }

    [TestMethod]
    public void Update_AtExpiryFrame_RemovesStatusEffectStackEntry()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Paralysis, StatusEffectSource.Admin));
        timers.Add(0, new ParalysisTimerComponent(1));
        var system = new ParalysisSystem(timers, stacks);

        system.Update(default, 0);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(stacks, 0, StatusEffectType.Paralysis));
    }

    [TestMethod]
    public void Update_BeforeExpiry_DoesNotRemoveStatusEffectStackEntry()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Paralysis, StatusEffectSource.Admin));
        timers.Add(0, new ParalysisTimerComponent(60));
        var system = new ParalysisSystem(timers, stacks);

        system.Update(default, 0);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(stacks, 0, StatusEffectType.Paralysis));
    }

    [TestMethod]
    public void Update_AfterExpiry_NextUpdateDoesNotThrow()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        timers.Add(0, new ParalysisTimerComponent(1));
        var system = new ParalysisSystem(timers, stacks);

        system.Update(default, 0);
        system.Update(default, 0);
    }

    /// <summary>
    /// ParalysisSystem's constructor takes no HealthComponent pool at all -- the concrete
    /// regression test that ticking Paralysis to expiry never needs, and never touches, hit
    /// points, unlike Burning/Poison's own systems.
    /// </summary>
    [TestMethod]
    public void Update_NoHealthComponentInvolvedAnywhere_TicksToExpiryWithoutError()
    {
        var timers = CreateTimerPool();
        var stacks = CreateStackPool();
        stacks.Add(0, new StatusEffectStack(StatusEffectType.Paralysis, StatusEffectSource.Admin));
        timers.Add(0, new ParalysisTimerComponent(1));
        var system = new ParalysisSystem(timers, stacks);

        system.Update(default, 0);

        Assert.IsFalse(timers.Has(0));
        Assert.AreEqual(0, StatusEffectQueries.CountStacks(stacks, 0, StatusEffectType.Paralysis));
    }
}
