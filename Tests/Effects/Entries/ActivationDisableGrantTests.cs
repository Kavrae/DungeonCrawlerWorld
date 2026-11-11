using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Tags;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Tags;

namespace Tests.Effects.Entries;

[TestClass]
public sealed class ActivationDisableGrantTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static ComponentManager Build() =>
        BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));

    private static EffectContext ContextOver(ComponentManager componentManager) => TestActionEffects.Context(
        SourceEntityId: SourceEntityId,
        TargetEntityId: TargetEntityId,
        Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
        EventBus: new EventBus(),
        MathUtility: new MathUtility(),
        ComponentManager: componentManager,
        EntityKeys: new EntityKeys(),
        ActivatorName: "Test",
        ActivatorTags: [], Now: 0,
        StatModifiers: componentManager.GetMultiPool<StatModifierComponent>());

    private static void HalveIncomingDebuffDurations(ComponentManager componentManager) =>
        componentManager.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingDebuffDuration, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, TestSources.Entity(TargetEntityId)));

    private static List<DisabledActivationComponent> RulesOf(ComponentManager componentManager)
    {
        var pool = componentManager.GetMultiPool<DisabledActivationComponent>();
        var rules = new List<DisabledActivationComponent>();
        for (var denseIndex = pool.GetFirstDenseIndex(TargetEntityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            rules.Add(pool.GetReadonlyByDenseIndex(denseIndex));
        }

        return rules;
    }

    [TestMethod]
    public void Apply_WithIncomingDebuffDurationOnTarget_ScalesTheDuration()
    {
        var componentManager = Build();
        HalveIncomingDebuffDurations(componentManager);
        var entry = new ActivationDisableGrant(GameplayTagQuery.Any([GameTags.Spell]), DurationFrames: 90);

        entry.Apply(ContextOver(componentManager));

        Assert.AreEqual(45u, RulesOf(componentManager).Single().ExpiresAtFrame);
    }

    [TestMethod]
    public void Apply_WithAFixedDuration_IsScaledByNothing_SoAReappliedGrantNeverLapses()
    {
        var componentManager = Build();
        HalveIncomingDebuffDurations(componentManager);
        var entry = new ActivationDisableGrant(GameplayTagQuery.Any([GameTags.Spell]), DurationFrames: 90, FixedDuration: true);

        entry.Apply(ContextOver(componentManager));

        Assert.AreEqual(90u, RulesOf(componentManager).Single().ExpiresAtFrame);
        Assert.IsEmpty(entry.AmountModifiers);
    }

    [TestMethod]
    public void Grant_FromTheSameQuery_ExtendsItsRule_AndAnotherQueryGetsAKeyOfItsOwn()
    {
        var componentManager = Build();
        var rules = componentManager.GetMultiPool<DisabledActivationComponent>();
        var spells = GameplayTagQuery.Any([GameTags.Spell]);
        var scrolls = GameplayTagQuery.Any([GameTags.ItemConsumableScroll]);

        DisabledActivations.Grant(rules, TargetEntityId, spells, expiresAtFrame: 100);
        DisabledActivations.Grant(rules, TargetEntityId, spells, expiresAtFrame: 160);
        DisabledActivations.Grant(rules, TargetEntityId, scrolls, expiresAtFrame: 100);

        var held = RulesOf(componentManager);
        Assert.HasCount(2, held);
        Assert.AreEqual(160u, held.Single(rule => ReferenceEquals(rule.Query, spells)).ExpiresAtFrame);
        Assert.AreNotEqual(held[0].InstanceKey, held[1].InstanceKey);
    }
}
