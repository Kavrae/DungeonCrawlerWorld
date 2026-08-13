using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Tests.Modules.Actions.Effects;

[TestClass]
public sealed class StatModifierGrantTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static ActionEffectContext BuildContext(ComponentManager componentManager, float durationScaleMultiplier) => new(
        SourceEntityId: SourceEntityId,
        TargetEntityId: TargetEntityId,
        Health: componentManager.GetPackedPool<HealthComponent>(),
        EventBus: new EventBus(),
        MathUtility: new MathUtility(),
        ComponentManager: componentManager,
        ActivatorName: "Test",
        ActivatorTags: [],
        StatModifiers: componentManager.GetMultiPool<StatModifierComponent>(),
        DurationScaleMultiplier: durationScaleMultiplier);

    private static ComponentManager Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatModifierComponent>();
        return componentManager;
    }

    private static StatModifierComponent GetGrantedModifier(ComponentManager componentManager)
    {
        var pool = componentManager.GetMultiPool<StatModifierComponent>();
        var denseIndex = pool.GetFirstDenseIndex(TargetEntityId);
        Assert.AreNotEqual(-1, denseIndex, "Expected a StatModifierComponent to have been granted.");
        return pool.GetReadonlyByDenseIndex(denseIndex);
    }

    [TestMethod]
    public void Apply_DurationScaleMultiplierAboveOne_ScalesDurationFrames()
    {
        var componentManager = Build();
        var entry = new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, CanModify: true, Magnitude: 1f, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 4.0f));

        Assert.AreEqual(400, GetGrantedModifier(componentManager).RemainingDurationFrames);
    }

    [TestMethod]
    public void Apply_DefaultDurationScaleMultiplier_LeavesDurationFramesUnchanged()
    {
        var componentManager = Build();
        var entry = new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, CanModify: true, Magnitude: 1f, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 1.0f));

        Assert.AreEqual(100, GetGrantedModifier(componentManager).RemainingDurationFrames);
    }

    [TestMethod]
    public void Apply_PermanentDuration_IsNeverScaled()
    {
        var componentManager = Build();
        var entry = new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, CanModify: true, Magnitude: 1f, DurationFrames: StatModifierComponent.Permanent);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 4.0f));

        Assert.AreEqual(StatModifierComponent.Permanent, GetGrantedModifier(componentManager).RemainingDurationFrames);
    }
}
