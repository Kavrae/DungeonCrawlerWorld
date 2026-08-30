using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Actions.Effects;

/// <summary>Covers DirectDamage's OutgoingDamage pass, including a Tag.Melee-conditional modifier (StatModifierComponent.ConditionTag) -- the generic mechanism BodyPartEffectsSystem's own Arm/Hand penalty now uses, see PLAN-body-part-gameplay-effects.md.</summary>
[TestClass]
public sealed class DirectDamageTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    /// <summary>Fixes both the damage roll and the crit roll -- MinFlatDamage/MaxFlatDamage are always equal in these tests, so Next(min, max+1) is deterministic regardless; NextDouble always returns 1.0, comfortably above any crit chance.</summary>
    private sealed class DeterministicRandom : Random
    {
        public override double NextDouble() => 1.0;
    }

    private static (ComponentManager ComponentManager, ActionEffectContext Context) Build(IReadOnlyList<Tag> activatorTags)
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<StatModifierComponent>();

        var health = componentManager.GetPackedPool<SimpleHealthComponent>();
        health.Add(TargetEntityId, new SimpleHealthComponent(currentHealth: 100, maximumHealth: 100));

        var context = new ActionEffectContext(
            SourceEntityId: SourceEntityId,
            TargetEntityId: TargetEntityId,
            Health: health,
            EventBus: new EventBus(),
            MathUtility: new MathUtility(new DeterministicRandom()),
            ComponentManager: componentManager,
            ActivatorName: "Test",
            ActivatorTags: activatorTags,
            StatModifiers: componentManager.GetMultiPool<StatModifierComponent>());

        return (componentManager, context);
    }

    /// <summary>Complex-health counterpart to Build -- a body-parts pool instead of SimpleHealthComponent, for BodyPartTargetMode.All/LowestPercentage coverage.</summary>
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
            MathUtility: new MathUtility(new DeterministicRandom()),
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
        var (componentManager, context) = Build([Tag.Attack]);

        new DirectDamage(MinFlatDamage: 10, MaxFlatDamage: 10, PercentageDamage: 0.2f).Apply(context);

        Assert.AreEqual(70f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "10 flat + 20% of 100 max health = 30 damage.");
    }

    [TestMethod]
    public void Apply_OutgoingDamageMeleeConditionalBuff_IncreasesMeleeDamageOnly()
    {
        var (melee, meleeContext) = Build([Tag.Melee, Tag.Attack]);
        melee.GetMultiPool<StatModifierComponent>().Add(SourceEntityId, new StatModifierComponent(
            StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: 0.10f, remainingDurationFrames: null, StatusEffectSource.FromEntity(SourceEntityId), Tag.Melee));
        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20).Apply(meleeContext);
        Assert.AreEqual(78f, melee.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "20 * 1.10 = 22 melee damage.");

        var (spell, spellContext) = Build([Tag.Spell, Tag.Attack]);
        spell.GetMultiPool<StatModifierComponent>().Add(SourceEntityId, new StatModifierComponent(
            StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: 0.10f, remainingDurationFrames: null, StatusEffectSource.FromEntity(SourceEntityId), Tag.Melee));
        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20).Apply(spellContext);
        Assert.AreEqual(80f, spell.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "A non-melee action must be untouched by the melee-only buff -- full 20 damage.");
    }

    [TestMethod]
    public void Apply_IncomingDamageMeleeConditionalDebuff_ReducesMeleeDamageOnly()
    {
        var (melee, meleeContext) = Build([Tag.Melee, Tag.Attack]);
        melee.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: -0.30f, remainingDurationFrames: null, StatusEffectSource.FromEntity(TargetEntityId), Tag.Melee));
        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20).Apply(meleeContext);
        Assert.AreEqual(86f, melee.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "20 * 0.70 = 14 melee damage taken.");

        var (spell, spellContext) = Build([Tag.Spell, Tag.Attack]);
        spell.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: -0.30f, remainingDurationFrames: null, StatusEffectSource.FromEntity(TargetEntityId), Tag.Melee));
        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20).Apply(spellContext);
        Assert.AreEqual(80f, spell.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "A non-melee hit must take the melee-only reduction's full, unreduced 20 damage.");
    }

    [TestMethod]
    public void Apply_IncomingDamageUnconditionalDebuff_ReducesDamageRegardlessOfTags()
    {
        var (melee, meleeContext) = Build([Tag.Melee, Tag.Attack]);
        melee.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: -0.05f, remainingDurationFrames: null, StatusEffectSource.FromEntity(TargetEntityId)));
        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20).Apply(meleeContext);
        Assert.AreEqual(81f, melee.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "20 * 0.95 = 19 melee damage taken.");

        var (spell, spellContext) = Build([Tag.Spell, Tag.Attack]);
        spell.GetMultiPool<StatModifierComponent>().Add(TargetEntityId, new StatModifierComponent(
            StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, canModify: false, magnitude: -0.05f, remainingDurationFrames: null, StatusEffectSource.FromEntity(TargetEntityId)));
        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20).Apply(spellContext);
        Assert.AreEqual(81f, spell.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "Unconditional -- a non-melee hit is reduced the same way.");
    }

    [TestMethod]
    public void Apply_BodyPartTargetModeAll_SplitsTotalEvenlyAcrossParts()
    {
        var (componentManager, context) = BuildComplex(
            [Tag.Attack],
            new BodyPartComponent("Head", BodyPartType.Head, partId: 0, verticalPosition: 5, currentHealth: 40, maximumHealth: 40, isVital: true),
            new BodyPartComponent("Torso", BodyPartType.Torso, partId: 1, verticalPosition: 4, currentHealth: 60, maximumHealth: 60, isVital: true));

        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20, BodyPartTargetMode: BodyPartTargetMode.All).Apply(context);

        var bodyParts = componentManager.GetMultiPool<BodyPartComponent>();
        HealthQueries.TryGetTotals(componentManager.GetPackedPool<SimpleHealthComponent>(), bodyParts, TargetEntityId, out var current, out _);
        Assert.AreEqual(80f, current, "20 total damage / 2 parts = 10 each, regardless of each part's own max health -- Head 40-10=30, Torso 60-10=50, total 80.");
    }

    [TestMethod]
    public void Apply_MeleeTagWithConditionalOutgoingDamageDebuff_ReducesDamage()
    {
        var (componentManager, context) = Build([Tag.Melee, Tag.Attack]);
        // -50% multiplicative debuff scoped to Tag.Melee -- the same shape BodyPartEffectsSystem grants for a damaged arm.
        componentManager.GetMultiPool<StatModifierComponent>().Add(SourceEntityId, new StatModifierComponent(
            StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.FromEntity(SourceEntityId), Tag.Melee));

        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20).Apply(context);

        Assert.AreEqual(90f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "20 base damage * 0.5x = 10 damage.");
    }

    [TestMethod]
    public void Apply_NoMeleeTag_MeleeConditionalOutgoingDamageDebuffHasNoEffect()
    {
        var (componentManager, context) = Build([Tag.Spell, Tag.Attack]);
        componentManager.GetMultiPool<StatModifierComponent>().Add(SourceEntityId, new StatModifierComponent(
            StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.FromEntity(SourceEntityId), Tag.Melee));

        new DirectDamage(MinFlatDamage: 20, MaxFlatDamage: 20).Apply(context);

        Assert.AreEqual(80f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "A non-melee action's damage must be untouched by a Tag.Melee-conditional modifier -- full 20 damage.");
    }
}
