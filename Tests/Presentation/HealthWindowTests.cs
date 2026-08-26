using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Health.Components;
using Game.Modules.Paralysis;
using Game.Modules.Paralysis.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;
using Presentation.UI;
using System.Linq;

namespace Tests.Presentation;

/// <summary>
/// Drives HealthWindow's pure data-assembly statics (BuildBodyPartRows/BuildStatusEffectRows)
/// directly against hand-built pools -- no GraphicsDevice-backed rendering pipeline needed, the
/// same shape InspectionWindowContentTests.ReplaceHealthEntriesWithEffectiveMaximum and
/// PlayerHealthHoverContentTests.BuildRows already use for their own body-part row assembly.
/// </summary>
[TestClass]
public sealed class HealthWindowTests
{
    private const int EntityId = 0;

    private static PackedComponentPool<SimpleHealthComponent> CreateHealthPool() =>
        new(maximumEntityCount: 10, initialCapacity: 4, static (ref existing, incoming) => existing = incoming);

    private static MultiComponentPool<BodyPartComponent> CreateBodyPartsPool() =>
        new(maximumEntityCount: 10, initialCapacity: 8);

    private static MultiComponentPool<StatModifierComponent> CreateMaximumHealthBuffPool(float magnitude)
    {
        var statModifiers = new MultiComponentPool<StatModifierComponent>(maximumEntityCount: 10, initialCapacity: 4);
        statModifiers.Add(EntityId, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: magnitude, remainingDurationFrames: null, StatusEffectSource.Admin));
        return statModifiers;
    }

    [TestMethod]
    public void BuildBodyPartRows_ComplexFixture_OneRowPerBodyPart()
    {
        var healthPool = CreateHealthPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(EntityId, new BodyPartComponent("Head", BodyPartType.Head, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: true));
        bodyParts.Add(EntityId, new BodyPartComponent("Torso", BodyPartType.Torso, verticalPosition: 0, currentHealth: 15, maximumHealth: 20, isVital: true));
        bodyParts.Add(EntityId, new BodyPartComponent("Left Arm", BodyPartType.Arm, verticalPosition: 0, currentHealth: 8, maximumHealth: 8, isVital: false));
        bodyParts.Add(EntityId, new BodyPartComponent("Right Arm", BodyPartType.Arm, verticalPosition: 0, currentHealth: 8, maximumHealth: 8, isVital: false));
        bodyParts.Add(EntityId, new BodyPartComponent("Left Leg", BodyPartType.Leg, verticalPosition: 0, currentHealth: 4, maximumHealth: 9, isVital: false));
        bodyParts.Add(EntityId, new BodyPartComponent("Right Leg", BodyPartType.Leg, verticalPosition: 0, currentHealth: 9, maximumHealth: 9, isVital: false));

        List<HealthWindow.BodyPartRow> rows = [];
        HealthWindow.BuildBodyPartRows(rows, EntityId, healthPool, bodyParts, statModifiers: null);

        Assert.HasCount(6, rows);
        var torsoRow = rows.Single(row => row.Name == "Torso");
        Assert.AreEqual(15f, torsoRow.CurrentHealth);
        Assert.AreEqual(20f, torsoRow.MaximumHealth);
    }

    [TestMethod]
    public void BuildBodyPartRows_MaximumHealthBuffActive_ShowsEffectiveMaximumNotRaw()
    {
        // A part sitting at its raw maximum (10/10) must still read below that once a +50%
        // MaximumHealth buff makes its true cap 15 -- regression for the same bug
        // ComplexHealthHeal/BodyPartSelection/PlayerHealthHoverContent had.
        var healthPool = CreateHealthPool();
        var bodyParts = CreateBodyPartsPool();
        bodyParts.Add(EntityId, new BodyPartComponent("Head", BodyPartType.Head, verticalPosition: 0, currentHealth: 10, maximumHealth: 10, isVital: true));
        var statModifiers = CreateMaximumHealthBuffPool(0.5f);

        List<HealthWindow.BodyPartRow> rows = [];
        HealthWindow.BuildBodyPartRows(rows, EntityId, healthPool, bodyParts, statModifiers);

        Assert.HasCount(1, rows);
        Assert.AreEqual(10f, rows[0].CurrentHealth);
        Assert.AreEqual(15f, rows[0].MaximumHealth, "Raw maximum is 10, but a +50% buff makes the effective maximum 15.");
    }

    [TestMethod]
    public void BuildBodyPartRows_SimpleHealthFixture_OneRowNamedHP()
    {
        var healthPool = CreateHealthPool();
        healthPool.Add(EntityId, new SimpleHealthComponent(currentHealth: 50, maximumHealth: 100));
        var bodyParts = CreateBodyPartsPool();

        List<HealthWindow.BodyPartRow> rows = [];
        HealthWindow.BuildBodyPartRows(rows, EntityId, healthPool, bodyParts, statModifiers: null);

        Assert.HasCount(1, rows);
        Assert.AreEqual("HP", rows[0].Name);
        Assert.AreEqual(50f, rows[0].CurrentHealth);
        Assert.AreEqual(100f, rows[0].MaximumHealth);
    }

    /// <summary>Fresh ComponentManager with StatusEffectStack plus every timer component pool registered -- BuildStatusEffectRows now reads timer durations through TimerBasedStatusEffectDisplay's own GetPackedPool lookup, so the pools have to live behind a real ComponentManager instead of standing alone.</summary>
    private static ComponentManager CreateComponentManagerWithStatusEffectPools()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<ParalysisTimerComponent>(static (ref existing, incoming) => { });
        return componentManager;
    }

    /// <summary>Mirrors PoisonModule/BurningModule/ParalysisModule's own real Configure registrations -- same formulas, just assembled directly instead of via GameBootstrapper.</summary>
    private static StatusEffectDisplayRegistry CreateStatusEffectDisplayRegistry()
    {
        var registry = new StatusEffectDisplayRegistry();
        registry.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph,
            poison => poison.FramesUntilNextTick + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames));
        registry.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph,
            burning => burning.FramesUntilNextTick + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
        registry.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph,
            paralysis => paralysis.FramesUntilNextTick));
        return registry;
    }

    [TestMethod]
    public void BuildStatusEffectRows_NoActiveEffects_Empty()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];

        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, componentManager.GetMultiPool<StatusEffectStack>(), CreateStatusEffectDisplayRegistry(), componentManager);

        Assert.IsEmpty(rows);
    }

    [TestMethod]
    public void BuildStatusEffectRows_PoisonActive_RemainingSecondsMatchesTimerFormula()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        componentManager.GetMultiPool<StatusEffectStack>().Add(EntityId, new StatusEffectStack(StatusEffectType.Poison, StatusEffectSource.Admin));
        // FramesUntilNextTick 30 + (RemainingDurationTicks 3 - 1) * TickIntervalFrames 60 = 150 frames = 2.5s -> ceil to 3.
        componentManager.GetPackedPool<PoisonTimerComponent>().Add(EntityId, new PoisonTimerComponent(framesUntilNextTick: 30, stackCount: 1, remainingDurationTicks: 3, StatusEffectSource.Admin));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, componentManager.GetMultiPool<StatusEffectStack>(), CreateStatusEffectDisplayRegistry(), componentManager);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatusEffectType.Poison, rows[0].Type);
        Assert.AreEqual(3, rows[0].RemainingSeconds);
    }

    [TestMethod]
    public void BuildStatusEffectRows_BurningActive_RemainingSecondsMatchesTimerFormula()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        componentManager.GetMultiPool<StatusEffectStack>().Add(EntityId, new StatusEffectStack(StatusEffectType.Burning, StatusEffectSource.Admin));
        // FramesUntilNextTick 45 + (StackCount 2 - 1) * TickIntervalFrames 60 = 105 frames = 1.75s -> ceil to 2.
        componentManager.GetPackedPool<BurningTimerComponent>().Add(EntityId, new BurningTimerComponent(framesUntilNextTick: 45, stackCount: 2));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, componentManager.GetMultiPool<StatusEffectStack>(), CreateStatusEffectDisplayRegistry(), componentManager);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatusEffectType.Burning, rows[0].Type);
        Assert.AreEqual(2, rows[0].RemainingSeconds);
    }

    [TestMethod]
    public void BuildStatusEffectRows_ParalysisActive_RemainingSecondsUsesFramesUntilNextTickDirectly()
    {
        var componentManager = CreateComponentManagerWithStatusEffectPools();
        componentManager.GetMultiPool<StatusEffectStack>().Add(EntityId, new StatusEffectStack(StatusEffectType.Paralysis, StatusEffectSource.Admin));
        // 61 frames = 1.017s -> ceil to 2, straight off FramesUntilNextTick (no repeating tick to add on top).
        componentManager.GetPackedPool<ParalysisTimerComponent>().Add(EntityId, new ParalysisTimerComponent(framesUntilNextTick: 61));

        List<HealthWindow.StatusEffectRow> rows = [];
        List<StatusEffectType> scratch = [];
        HealthWindow.BuildStatusEffectRows(rows, scratch, EntityId, componentManager.GetMultiPool<StatusEffectStack>(), CreateStatusEffectDisplayRegistry(), componentManager);

        Assert.HasCount(1, rows);
        Assert.AreEqual(StatusEffectType.Paralysis, rows[0].Type);
        Assert.AreEqual(2, rows[0].RemainingSeconds);
    }
}
