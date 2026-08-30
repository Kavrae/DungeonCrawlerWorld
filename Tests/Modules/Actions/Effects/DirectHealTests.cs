using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Actions.Effects;

/// <summary>Covers DirectHeal's flat+percent combination and its OutgoingHealing/IncomingHealing modifier passes -- the healing counterpart to DirectDamageTests.</summary>
[TestClass]
public sealed class DirectHealTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    private static (ComponentManager ComponentManager, ActionEffectContext Context) Build(IReadOnlyList<Tag> activatorTags, ushort currentHealth = 50, ushort maximumHealth = 100, MultiComponentPool<AbilityScoreComponent>? abilityScores = null)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatModifierComponent>();

        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        health.Add(TargetEntityId, new SimpleHealthComponent(currentHealth, maximumHealth));

        var context = new ActionEffectContext(
            SourceEntityId: SourceEntityId,
            TargetEntityId: TargetEntityId,
            Health: health,
            EventBus: new EventBus(),
            MathUtility: new MathUtility(new Random()),
            ComponentManager: componentManager,
            ActivatorName: "Test",
            ActivatorTags: activatorTags,
            StatModifiers: componentManager.GetMultiPool<StatModifierComponent>(),
            AbilityScores: abilityScores);

        return (componentManager, context);
    }

    private static (ComponentManager ComponentManager, ActionEffectContext Context) BuildComplex(IReadOnlyList<Tag> activatorTags, params BodyPartComponent[] parts)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatModifierComponent>();
        componentManager.RegisterMultiPool<BodyPartComponent>();

        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        foreach (var part in parts)
        {
            bodyParts.Add(TargetEntityId, part);
        }

        var context = new ActionEffectContext(
            SourceEntityId: SourceEntityId,
            TargetEntityId: TargetEntityId,
            Health: componentManager.GetPackedPool<SimpleHealthComponent>(),
            EventBus: new EventBus(),
            MathUtility: new MathUtility(new Random()),
            ComponentManager: componentManager,
            ActivatorName: "Test",
            ActivatorTags: activatorTags,
            StatModifiers: componentManager.GetMultiPool<StatModifierComponent>(),
            BodyParts: bodyParts);

        return (componentManager, context);
    }

    [TestMethod]
    public void Apply_PercentOfMaxHealth_AddsFlatAndPercentTogether()
    {
        var (componentManager, context) = Build([Tag.Healing]);

        new DirectHeal(PercentOfMaxHealth: 0.2f, FlatAmount: 10f).Apply(context);

        Assert.AreEqual(80f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "50 current + (10 flat + 20% of 100 max = 30) = 80.");
    }

    [TestMethod]
    public void Apply_OutgoingHealingBuff_IncreasesHealGiven()
    {
        var (componentManager, context) = Build([Tag.Healing]);
        componentManager.GetMultiPool<StatModifierComponent>().Add(SourceEntityId, new StatModifierComponent(
            StatModifierTarget.OutgoingHealing, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: 0.05f, remainingDurationFrames: null, StatusEffectSource.FromEntity(SourceEntityId)));

        new DirectHeal(PercentOfMaxHealth: 0.2f).Apply(context);

        Assert.AreEqual(71f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "50 + (20 * 1.05 = 21) = 71.");
    }

    [TestMethod]
    public void Apply_IncomingHealingBuff_IncreasesHealReceived()
    {
        var (componentManager, context) = Build([Tag.Healing]);
        componentManager.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingHealing, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: 0.20f, remainingDurationFrames: null, StatusEffectSource.FromEntity(TargetEntityId)));

        new DirectHeal(PercentOfMaxHealth: 0.2f).Apply(context);

        Assert.AreEqual(74f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "50 + (20 * 1.20 = 24) = 74.");
    }

    [TestMethod]
    public void Apply_AbilityScoreTaggedHeal_AddsCastersMatchingAbilityScoreTotal()
    {
        var abilityScores = new MultiComponentPool<AbilityScoreComponent>(maximumEntityCount: 10, initialCapacity: 4);
        abilityScores.Add(SourceEntityId, new AbilityScoreComponent(AbilityScoreType.Wisdom, baseValue: 15, total: 15));
        var (componentManager, context) = Build([Tag.Healing, Tag.Wisdom], abilityScores: abilityScores);

        new DirectHeal(PercentOfMaxHealth: 0f, FlatAmount: 10f).Apply(context);

        Assert.AreEqual(75f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "50 + (10 flat + caster's own Wisdom total of 15) = 75.");
    }

    [TestMethod]
    public void Apply_BodyPartTargetModeLowestPercentage_HealsOnlyTheMostDamagedPart()
    {
        var (componentManager, context) = BuildComplex(
            [Tag.Healing],
            new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, currentHealth: 90, maximumHealth: 100, isVital: true),
            new BodyPartComponent("Leg", BodyPartType.Leg, partId: 1, verticalPosition: 1, currentHealth: 20, maximumHealth: 100, isVital: false));

        new DirectHeal(PercentOfMaxHealth: 0.1f, BodyPartTargetMode: BodyPartTargetMode.LowestPercentage).Apply(context);

        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        var headDenseIndex = BodyPartSelection.PickByType(bodyParts, TargetEntityId, BodyPartType.Head);
        var legDenseIndex = BodyPartSelection.PickByType(bodyParts, TargetEntityId, BodyPartType.Leg);
        Assert.AreEqual(90f, bodyParts.GetReadonlyByDenseIndex(headDenseIndex).CurrentHealth, "Untouched -- only the most-damaged part is healed.");
        Assert.AreEqual(40f, bodyParts.GetReadonlyByDenseIndex(legDenseIndex).CurrentHealth, "10% of the overall max (100+100=200) = 20, applied entirely to the Leg: 20 + 20 = 40.");
    }
}
