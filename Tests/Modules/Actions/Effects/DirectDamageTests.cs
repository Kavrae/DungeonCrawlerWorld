using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.Actions.Effects;

/// <summary>Covers DirectDamage's additional Tag.Melee-only MeleeOutgoingDamage pass -- BodyPartEffectsSystem's own Arm/Hand penalty, see PLAN-body-part-gameplay-effects.md.</summary>
[TestClass]
public sealed class DirectDamageTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    /// <summary>Fixes both the damage roll and the crit roll -- MinAmount/MaxAmount are always equal in these tests, so Next(min, max+1) is deterministic regardless; NextDouble always returns 1.0, comfortably above any crit chance.</summary>
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

    [TestMethod]
    public void Apply_MeleeTagWithMeleeOutgoingDamageDebuff_ReducesDamageOnTopOfOutgoingDamage()
    {
        var (componentManager, context) = Build([Tag.Melee, Tag.Attack]);
        // -50% multiplicative debuff -- the same shape BodyPartEffectsSystem grants for a damaged arm.
        componentManager.GetMultiPool<StatModifierComponent>().Add(SourceEntityId, new StatModifierComponent(
            StatModifierTarget.MeleeOutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.FromEntity(SourceEntityId)));

        new DirectDamage(MinAmount: 20, MaxAmount: 20).Apply(context);

        Assert.AreEqual(90f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "20 base damage * 0.5x = 10 damage.");
    }

    [TestMethod]
    public void Apply_NoMeleeTag_MeleeOutgoingDamageDebuffHasNoEffect()
    {
        var (componentManager, context) = Build([Tag.Spell, Tag.Attack]);
        componentManager.GetMultiPool<StatModifierComponent>().Add(SourceEntityId, new StatModifierComponent(
            StatModifierTarget.MeleeOutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, canModify: false, magnitude: -0.5f, remainingDurationFrames: null, StatusEffectSource.FromEntity(SourceEntityId)));

        new DirectDamage(MinAmount: 20, MaxAmount: 20).Apply(context);

        Assert.AreEqual(80f, componentManager.GetPackedPool<SimpleHealthComponent>().GetReadonly(TargetEntityId).CurrentHealth, "A non-melee action's damage must be untouched by MeleeOutgoingDamage -- full 20 damage.");
    }
}
