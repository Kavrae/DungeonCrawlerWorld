using Engine.ECS.Systems;
using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Game.World;

namespace Tests.Modules.Actions.Effects;

[TestClass]
public sealed class StatusEffectImmunityGrantTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static ActionEffectContext BuildContext(ComponentManager componentManager, float durationScaleMultiplier = 1.0f) => new(
        SourceEntityId: SourceEntityId,
        TargetEntityId: TargetEntityId,
        Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
        EventBus: new EventBus(),
        MathUtility: new MathUtility(),
        ComponentManager: componentManager,
        ActivatorName: "Test",
        ActivatorTags: [], Now: 0,
        StatModifiers: componentManager.GetMultiPool<StatModifierComponent>(),
        DurationScaleMultiplier: durationScaleMultiplier);

    private static ComponentManager Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatModifierComponent>();
        componentManager.RegisterMultiPool<StatusEffectImmunityComponent>();
        return componentManager;
    }

    private static StatusEffectImmunityComponent GetGrantedImmunity(ComponentManager componentManager)
    {
        var pool = componentManager.GetMultiPool<StatusEffectImmunityComponent>();
        var denseIndex = pool.GetFirstDenseIndex(TargetEntityId);
        Assert.AreNotEqual(-1, denseIndex, "Expected a StatusEffectImmunityComponent to have been granted.");
        return pool.GetReadonlyByDenseIndex(denseIndex);
    }

    [TestMethod]
    public void Apply_GrantsImmunityForTheGivenEffectType()
    {
        var componentManager = Build();
        var entry = new StatusEffectImmunityGrant(StatusEffectType.Burning, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager));

        var immunity = GetGrantedImmunity(componentManager);
        Assert.AreEqual(StatusEffectType.Burning, immunity.EffectType);
        Assert.AreEqual(100u, immunity.ExpiresAtFrame);
    }

    [TestMethod]
    public void Apply_NullDuration_GrantsPermanentImmunity()
    {
        var componentManager = Build();
        var entry = new StatusEffectImmunityGrant(StatusEffectType.Poison);

        entry.Apply(BuildContext(componentManager));

        Assert.AreEqual(FrameDeadline.Never, GetGrantedImmunity(componentManager).ExpiresAtFrame, "Permanent.");
    }

    [TestMethod]
    public void Apply_DurationScaleMultiplierAboveOne_ScalesDurationFrames()
    {
        var componentManager = Build();
        var entry = new StatusEffectImmunityGrant(StatusEffectType.Burning, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager, durationScaleMultiplier: 4.0f));

        Assert.AreEqual(400u, GetGrantedImmunity(componentManager).ExpiresAtFrame);
    }

    [TestMethod]
    public void Apply_IncomingBuffDurationOnTarget_ScalesDuration()
    {
        var componentManager = Build();
        componentManager.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingBuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: 0.5f, expiresAtFrame: FrameDeadline.Never, StatusEffectSource.FromEntity(TargetEntityId)));
        var entry = new StatusEffectImmunityGrant(StatusEffectType.Burning, DurationFrames: 100);

        entry.Apply(BuildContext(componentManager));

        Assert.AreEqual(150u, GetGrantedImmunity(componentManager).ExpiresAtFrame, "100 * (1 + 0.5) = 150 -- immunity is a Buff, so IncomingBuffDuration applies (not IncomingDebuffDuration).");
    }
}
