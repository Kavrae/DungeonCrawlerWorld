using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Systems;
using Game.Modules.Core.Components;
using Game.Modules.Paralysis;
using Game.Modules.Paralysis.Components;
using Game.Modules.Paralysis.Systems;
using Game.World;

namespace Tests.Modules.Paralysis;

[TestClass]
public sealed class ParalysisSystemTests
{
    private static EngineTime Frame(long frame) => new(default, default, false, frame);

    private static PackedComponentPool<ParalysisTimerComponent> CreateTimerPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => { });

    /// <summary>Runs every frame from..to inclusive -- the way SystemManager drives it.</summary>
    private static void Run(ParalysisSystem system, long from, long to)
    {
        for (var frame = from; frame <= to; frame++)
        {
            system.Update(Frame(frame), 0);
        }
    }

    [TestMethod]
    public void BeforeItsExpiryFrame_TimerStays()
    {
        var timers = CreateTimerPool();
        timers.Add(0, new ParalysisTimerComponent(expiresAtFrame: 60));
        var system = new ParalysisSystem(timers);

        Run(system, 0, 59);

        Assert.IsTrue(timers.Has(0));
    }

    [TestMethod]
    public void OnItsExpiryFrame_TimerIsRemoved()
    {
        var timers = CreateTimerPool();
        timers.Add(0, new ParalysisTimerComponent(expiresAtFrame: 60));
        var system = new ParalysisSystem(timers);

        Run(system, 0, 60);

        Assert.IsFalse(timers.Has(0));
    }

    /// <summary>An update that jumps straight past the expiry still fires it -- late, never lost.</summary>
    [TestMethod]
    public void UpdateJumpingPastExpiry_StillRemovesTimer()
    {
        var timers = CreateTimerPool();
        timers.Add(0, new ParalysisTimerComponent(expiresAtFrame: 60));
        var system = new ParalysisSystem(timers);

        system.Update(Frame(500), 0);

        Assert.IsFalse(timers.Has(0));
    }

    [TestMethod]
    public void AfterExpiry_FurtherUpdatesDoNotThrow()
    {
        var timers = CreateTimerPool();
        timers.Add(0, new ParalysisTimerComponent(expiresAtFrame: 1));
        var system = new ParalysisSystem(timers);

        Run(system, 0, 10);

        Assert.IsFalse(timers.Has(0));
    }

    /// <summary>
    /// Re-applying Paralysis mid-way pushes the expiry out -- and ParalysisSystem picks up the new
    /// frame without anyone telling it, because the pool reports the write to its timer wheel.
    /// </summary>
    [TestMethod]
    public void ReappliedMidway_ExpiresAtTheLaterFrame()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<ParalysisTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        var timers = componentManager.GetPackedPool<ParalysisTimerComponent>();
        var system = new ParalysisSystem(timers);

        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin, now: 0);
        Run(system, 0, 100);
        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin, now: 100);

        Run(system, 101, 100 + ParalysisEffects.DurationFrames - 1);
        Assert.IsTrue(timers.Has(0), "Still paralyzed until DurationFrames after the second application, not the first.");

        system.Update(Frame(100 + ParalysisEffects.DurationFrames), 0);
        Assert.IsFalse(timers.Has(0));
    }

    /// <summary>
    /// ParalysisSystem's constructor takes no SimpleHealthComponent pool at all -- the concrete
    /// regression test that ticking Paralysis to expiry never needs, and never touches, hit
    /// points, unlike Burning/Poison's own systems.
    /// </summary>
    [TestMethod]
    public void NoHealthComponentInvolvedAnywhere_ExpiresWithoutError()
    {
        var timers = CreateTimerPool();
        timers.Add(0, new ParalysisTimerComponent(expiresAtFrame: 1));
        var system = new ParalysisSystem(timers);

        Run(system, 0, 1);

        Assert.IsFalse(timers.Has(0));
    }
}
