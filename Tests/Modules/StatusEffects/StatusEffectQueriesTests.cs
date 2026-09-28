using Engine.ECS.Systems;
using Engine.ECS.Components;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.StatusEffects;
using Game.World;

namespace Tests.Modules.StatusEffects;

[TestClass]
public sealed class StatusEffectQueriesTests
{
    private static ComponentManager CreateComponentManager()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        return componentManager;
    }

    /// <summary>Mirrors BurningModule.Configure's own registration -- the real path GetStackCount reads through.</summary>
    private static StatusEffectDisplayRegistry CreateDisplaysWithBurningRegistered(ComponentManager componentManager)
    {
        var displays = new StatusEffectDisplayRegistry();
        displays.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph, componentManager.GetPackedPool<BurningTimerComponent>(),
            (burning, now) => FrameDeadline.Remaining(burning.NextTickFrame, now) + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
        return displays;
    }

    [TestMethod]
    public void HasStack_NoTimer_ReturnsFalse()
    {
        var componentManager = CreateComponentManager();
        var displays = CreateDisplaysWithBurningRegistered(componentManager);

        Assert.IsFalse(StatusEffectQueries.HasStack(displays, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void HasStack_ActiveTimer_ReturnsTrue()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetPackedPool<BurningTimerComponent>().Add(0, new BurningTimerComponent(60, stackCount: 1, ActionSource.Admin));
        var displays = CreateDisplaysWithBurningRegistered(componentManager);

        Assert.IsTrue(StatusEffectQueries.HasStack(displays, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void HasStack_NoDisplayRegisteredForType_ReturnsFalse()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetPackedPool<BurningTimerComponent>().Add(0, new BurningTimerComponent(60, stackCount: 1, ActionSource.Admin));
        var displays = new StatusEffectDisplayRegistry();

        Assert.IsFalse(StatusEffectQueries.HasStack(displays, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void CountStacks_ReadsTimersOwnStackCount()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetPackedPool<BurningTimerComponent>().Add(0, new BurningTimerComponent(60, stackCount: 4, ActionSource.Admin));
        var displays = CreateDisplaysWithBurningRegistered(componentManager);

        Assert.AreEqual(4, StatusEffectQueries.CountStacks(displays, 0, StatusEffectType.Burning));
    }

    [TestMethod]
    public void CountStacks_DifferentEntity_IsIndependent()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetPackedPool<BurningTimerComponent>().Add(0, new BurningTimerComponent(60, stackCount: 4, ActionSource.Admin));
        var displays = CreateDisplaysWithBurningRegistered(componentManager);

        Assert.AreEqual(0, StatusEffectQueries.CountStacks(displays, 1, StatusEffectType.Burning));
    }

    [TestMethod]
    public void GetActiveEffectTypes_NoActiveTimers_FillsEmpty()
    {
        var componentManager = CreateComponentManager();
        var displays = CreateDisplaysWithBurningRegistered(componentManager);
        var destination = new List<StatusEffectType> { StatusEffectType.Burning };

        StatusEffectQueries.GetActiveEffectTypes(displays, 0, destination);

        Assert.IsEmpty(destination);
    }

    [TestMethod]
    public void GetActiveEffectTypes_OneActiveTimer_ReturnsItsType()
    {
        var componentManager = CreateComponentManager();
        componentManager.GetPackedPool<BurningTimerComponent>().Add(0, new BurningTimerComponent(60, stackCount: 1, ActionSource.Admin));
        var displays = CreateDisplaysWithBurningRegistered(componentManager);
        var destination = new List<StatusEffectType>();

        StatusEffectQueries.GetActiveEffectTypes(displays, 0, destination);

        CollectionAssert.AreEqual(new[] { StatusEffectType.Burning }, destination);
    }
}
