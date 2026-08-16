using Engine.ECS.Components;
using Game.Modules.Core.Components;
using Game.Modules.Paralysis;
using Game.Modules.Paralysis.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Paralysis;

[TestClass]
public sealed class ParalysisEffectsTests
{
    private static ComponentManager CreateComponentManager()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<ParalysisTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.GetPackedPool<ActionLockComponent>().Add(0, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, currentLockFramesRemaining: 0));
        return componentManager;
    }

    [TestMethod]
    public void Apply_NewEntity_AddsAStack()
    {
        var componentManager = CreateComponentManager();

        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), 0, StatusEffectType.Paralysis));
    }

    [TestMethod]
    public void Apply_NewEntity_CreatesTimerWithDurationFrames()
    {
        var componentManager = CreateComponentManager();

        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);

        var timer = componentManager.GetPackedPool<ParalysisTimerComponent>().GetReadonly(0);
        Assert.AreEqual(ParalysisEffects.DurationFrames, timer.FramesUntilNextTick);
    }

    [TestMethod]
    public void Apply_NewEntity_LocksActionLockComponentForDurationFrames()
    {
        var componentManager = CreateComponentManager();

        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);

        var actionLock = componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(0);
        Assert.AreEqual(ParalysisEffects.DurationFrames, actionLock.CurrentLockFramesRemaining);
    }

    [TestMethod]
    public void Apply_WhileAlreadyParalyzed_DoesNotAddASecondStack()
    {
        var componentManager = CreateComponentManager();
        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);

        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(1, StatusEffectQueries.CountStacks(componentManager.GetMultiPool<StatusEffectStack>(), 0, StatusEffectType.Paralysis));
    }

    /// <summary>Refreshes to the greater of what remained and DurationFrames -- never additive, mirroring PoisonEffects.ApplyStack's own duration rule.</summary>
    [TestMethod]
    public void Apply_WhileAlreadyParalyzedWithLessTimeRemaining_RefreshesToDurationFrames()
    {
        var componentManager = CreateComponentManager();
        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);
        componentManager.GetPackedPool<ParalysisTimerComponent>().TryUpdate(0, static (ref ParalysisTimerComponent t) => t.FramesUntilNextTick = 5);

        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(ParalysisEffects.DurationFrames, componentManager.GetPackedPool<ParalysisTimerComponent>().GetReadonly(0).FramesUntilNextTick);
    }

    [TestMethod]
    public void Apply_WhileAlreadyParalyzedWithLessTimeRemaining_RefreshesActionLockToDurationFrames()
    {
        var componentManager = CreateComponentManager();
        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);
        componentManager.GetPackedPool<ActionLockComponent>().TryUpdate(0, static (ref ActionLockComponent a) => a.CurrentLockFramesRemaining = 5);

        ParalysisEffects.Apply(componentManager, 0, StatusEffectSource.Admin);

        Assert.AreEqual(ParalysisEffects.DurationFrames, componentManager.GetPackedPool<ActionLockComponent>().GetReadonly(0).CurrentLockFramesRemaining);
    }
}
