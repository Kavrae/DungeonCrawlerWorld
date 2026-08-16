using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Effects;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;

namespace Tests.Modules.Actions;

/// <summary>
/// Focused coverage for the new composable entry types this plan introduced, exercised directly
/// against IActionEffectEntry.Apply rather than only indirectly through ActionEffectResolver/
/// ConsumableActivationSystem -- these are now the real unit of shared behavior.
/// </summary>
[TestClass]
public sealed class ActionEffectTests
{
    private const int SourceEntityId = 1;
    private const int TargetEntityId = 2;

    /// <summary>Always rolls a crit/always triggers a chain -- NextDouble always returns 0.0.</summary>
    private sealed class AlwaysCritRandom : Random
    {
        public override double NextDouble() => 0.0;
    }

    /// <summary>Never rolls a crit/never triggers a chain -- NextDouble always returns 1.0.</summary>
    private sealed class NeverCritRandom : Random
    {
        public override double NextDouble() => 1.0;
    }

    private static (ComponentManager ComponentManager, PackedComponentPool<HealthComponent> Health, EventBus EventBus) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10);
        componentManager.RegisterPackedPool<HealthComponent>(static (ref existing, incoming) => existing = incoming);
        return (componentManager, componentManager.GetPackedPool<HealthComponent>(), new EventBus());
    }

    private static ActionEffectContext Context(
        ComponentManager componentManager,
        PackedComponentPool<HealthComponent> health,
        EventBus eventBus,
        MathUtility mathUtility,
        MultiComponentPool<StatModifierComponent>? statModifiers = null,
        ushort? damageOverride = null) =>
        new(SourceEntityId, TargetEntityId, health, eventBus, mathUtility, componentManager, "Test Action", ActivatorTags: [], StatModifiers: statModifiers, DamageOverride: damageOverride);

    [TestMethod]
    public void DirectDamage_RollsWithinMinMaxRange_WhenNoOverride()
    {
        var (componentManager, health, eventBus) = Build();
        health.Add(TargetEntityId, new HealthComponent(100, 100));
        var mathUtility = new MathUtility(new NeverCritRandom());

        new DirectDamage(MinAmount: 10, MaxAmount: 10).Apply(Context(componentManager, health, eventBus, mathUtility));

        Assert.AreEqual(90, health.GetReadonly(TargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void DirectDamage_DamageOverride_BypassesTheRoll()
    {
        var (componentManager, health, eventBus) = Build();
        health.Add(TargetEntityId, new HealthComponent(100, 100));
        var mathUtility = new MathUtility(new NeverCritRandom());

        new DirectDamage(MinAmount: 999, MaxAmount: 999).Apply(Context(componentManager, health, eventBus, mathUtility, damageOverride: 10));

        Assert.AreEqual(90, health.GetReadonly(TargetEntityId).CurrentHealth, "The 999-999 catalog range must be ignored entirely once DamageOverride is set.");
    }

    [TestMethod]
    public void DirectDamage_CritRoll_MultipliesTheFullyScaledResult()
    {
        var (componentManager, health, eventBus) = Build();
        health.Add(TargetEntityId, new HealthComponent(100, 100));
        var mathUtility = new MathUtility(new AlwaysCritRandom());

        new DirectDamage(MinAmount: 10, MaxAmount: 10).Apply(Context(componentManager, health, eventBus, mathUtility));

        // 10 base * CritMath.BaseCritMultiplier (3x) = 30.
        Assert.AreEqual(70, health.GetReadonly(TargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void DirectDamage_ZeroBaseAmount_DoesNothing()
    {
        var (componentManager, health, eventBus) = Build();
        health.Add(TargetEntityId, new HealthComponent(100, 100));
        var mathUtility = new MathUtility(new NeverCritRandom());

        new DirectDamage(MinAmount: 0, MaxAmount: 0).Apply(Context(componentManager, health, eventBus, mathUtility));

        Assert.AreEqual(100, health.GetReadonly(TargetEntityId).CurrentHealth);
    }

    [TestMethod]
    public void StatModifierGrant_LandsOnTargetEntityNotSourceEntity()
    {
        var (componentManager, health, eventBus) = Build();
        componentManager.RegisterMultiPool<StatModifierComponent>();
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var mathUtility = new MathUtility();
        var entry = new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff, CanModify: true, Magnitude: 5f, DurationFrames: 60);

        entry.Apply(Context(componentManager, health, eventBus, mathUtility, statModifiers));

        Assert.AreEqual(5f, StatModifierMath.GetEffectiveValue(statModifiers, TargetEntityId, StatModifierTarget.OutgoingDamage, 0f));
        Assert.AreEqual(0f, StatModifierMath.GetEffectiveValue(statModifiers, SourceEntityId, StatModifierTarget.OutgoingDamage, 0f));
    }

    /// <summary>A caller that wants to buff the caster itself does so via a Self-shaped TargetingSpec, which resolves TargetEntityId to the caster -- not by anything StatModifierGrant itself does with SourceEntityId.</summary>
    [TestMethod]
    public void StatModifierGrant_SourceAndTargetAreSameEntity_LandsOnThatEntity()
    {
        var (componentManager, health, eventBus) = Build();
        componentManager.RegisterMultiPool<StatModifierComponent>();
        var statModifiers = componentManager.GetMultiPool<StatModifierComponent>();
        var mathUtility = new MathUtility();
        var entry = new StatModifierGrant(StatModifierTarget.CritChance, StatModifierOperation.Additive, StatModifierPolarity.Buff, CanModify: true, Magnitude: 0.5f, DurationFrames: 60);
        var context = Context(componentManager, health, eventBus, mathUtility, statModifiers) with { TargetEntityId = SourceEntityId };

        entry.Apply(context);

        Assert.AreEqual(0.5f, StatModifierMath.GetEffectiveValue(statModifiers, SourceEntityId, StatModifierTarget.CritChance, 0f));
    }

    [TestMethod]
    public void ChainedEffect_TriggerRolled_AppliesAllTriggeredEffectsInOrder()
    {
        var (componentManager, health, eventBus) = Build();
        health.Add(TargetEntityId, new HealthComponent(currentHealth: 10, maximumHealth: 100));
        var mathUtility = new MathUtility(new AlwaysCritRandom()); // NextDouble() -> 0.0, always below TriggerChance.
        // DirectHeal, not DirectDamage -- AlwaysCritRandom would also force every nested
        // damage roll to crit, coupling this test to crit math it isn't trying to exercise.
        ActionEffect[] triggered = [new([new DirectHeal(0.1f)]), new([new DirectHeal(0.1f)])];

        new ChainedEffect(TriggerChance: 1f, TriggeredEffects: triggered).Apply(Context(componentManager, health, eventBus, mathUtility));

        // Two 10%-of-100-max heals from CurrentHealth 10: 10 -> 20 -> 30.
        Assert.AreEqual(30, health.GetReadonly(TargetEntityId).CurrentHealth, "Both triggered ActionEffects must apply.");
    }

    [TestMethod]
    public void ChainedEffect_TriggerNotRolled_DoesNothing()
    {
        var (componentManager, health, eventBus) = Build();
        health.Add(TargetEntityId, new HealthComponent(100, 100));
        var mathUtility = new MathUtility(new NeverCritRandom());
        ActionEffect[] triggered = [new([new DirectDamage(5, 5)])];

        new ChainedEffect(TriggerChance: 0.5f, TriggeredEffects: triggered).Apply(Context(componentManager, health, eventBus, mathUtility));

        Assert.AreEqual(100, health.GetReadonly(TargetEntityId).CurrentHealth);
    }

    /// <summary>MaxChainDepth guards the same failure mode WoW/PoE explicitly design around: a proc that (directly or via a longer cycle) triggers itself.</summary>
    [TestMethod]
    public void ChainedEffect_SelfReferentialChain_TerminatesInsteadOfRecursingForever()
    {
        var (componentManager, health, eventBus) = Build();
        health.Add(TargetEntityId, new HealthComponent(100, 100));
        var mathUtility = new MathUtility(new AlwaysCritRandom());

        List<IActionEffectEntry> entries = [];
        var selfEffect = new ActionEffect(entries);
        var selfChain = new ChainedEffect(TriggerChance: 1f, TriggeredEffects: [selfEffect]);
        entries.Add(selfChain);

        selfChain.Apply(Context(componentManager, health, eventBus, mathUtility));
    }

    [TestMethod]
    public void ActionEffectSequence_MultipleEffects_AppliesAllInOrder()
    {
        var (componentManager, health, eventBus) = Build();
        health.Add(TargetEntityId, new HealthComponent(100, 100));
        var mathUtility = new MathUtility(new NeverCritRandom());
        ActionEffect[] effects = [new([new DirectDamage(5, 5)]), new([new DirectDamage(5, 5)])];

        ActionEffectSequence.Apply(effects, Context(componentManager, health, eventBus, mathUtility));

        Assert.AreEqual(90, health.GetReadonly(TargetEntityId).CurrentHealth);
    }
}
