using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Actions.Effects;

[TestClass]
public sealed class StatModifierGrantTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static ActionEffectContext BuildContext(ComponentManager componentManager, float durationScaleMultiplier) => new(
        SourceEntityId: SourceEntityId,
        TargetEntityId: TargetEntityId,
        Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
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
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
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

        Assert.AreEqual((ushort?)400, GetGrantedModifier(componentManager).RemainingDurationFrames);
    }

    [TestMethod]
    public void Apply_DefaultDurationScaleMultiplier_LeavesDurationFramesUnchanged()
    {
        var componentManager = Build();
        var entry = new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, CanModify: true, Magnitude: 1f, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 1.0f));

        Assert.AreEqual((ushort?)100, GetGrantedModifier(componentManager).RemainingDurationFrames);
    }

    [TestMethod]
    public void Apply_PermanentDuration_IsNeverScaled()
    {
        var componentManager = Build();
        var entry = new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, CanModify: true, Magnitude: 1f, DurationFrames: null);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 4.0f));

        Assert.IsNull(GetGrantedModifier(componentManager).RemainingDurationFrames);
    }

    [TestMethod]
    public void Apply_DebuffGrantWithOutgoingDebuffDurationOnCaster_ScalesDuration()
    {
        var componentManager = Build();
        componentManager.GetMultiPool<StatModifierComponent>().Add(SourceEntityId, new StatModifierComponent(
            StatModifierTarget.OutgoingDebuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: 1.0f, remainingDurationFrames: null, StatusEffectSource.FromEntity(SourceEntityId)));
        var entry = new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, CanModify: false, Magnitude: 0.1f, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 1.0f));

        Assert.AreEqual((ushort?)200, GetGrantedModifier(componentManager).RemainingDurationFrames, "100 * (1 + 1.0) = 200.");
    }

    [TestMethod]
    public void Apply_DebuffGrantWithIncomingDebuffDurationOnTarget_ScalesDuration()
    {
        var componentManager = Build();
        componentManager.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingDebuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.FromEntity(TargetEntityId)));
        var entry = new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, CanModify: false, Magnitude: 0.1f, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 1.0f));

        Assert.AreEqual((ushort?)50, GetGrantedModifier(componentManager).RemainingDurationFrames, "100 * (1 - 0.5) = 50 -- debuffs against the target expire faster.");
    }

    [TestMethod]
    public void Apply_BuffGrant_UsesBuffDurationTargetsNotDebuff()
    {
        var componentManager = Build();
        // Scoped to Debuff -- must have zero effect on this Buff-polarity grant.
        componentManager.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingDebuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.9f, remainingDurationFrames: null, StatusEffectSource.FromEntity(TargetEntityId)));
        componentManager.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingBuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: 0.5f, remainingDurationFrames: null, StatusEffectSource.FromEntity(TargetEntityId)));
        var entry = new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, CanModify: true, Magnitude: 1f, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 1.0f));

        Assert.AreEqual((ushort?)150, GetGrantedModifier(componentManager).RemainingDurationFrames, "100 * (1 + 0.5) = 150 -- the IncomingDebuffDuration modifier must not apply to a Buff grant.");
    }
}
