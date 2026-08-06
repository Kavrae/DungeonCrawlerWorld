using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Tests.Modules.AbilityScores;

[TestClass]
public sealed class AbilityScoreEffectsTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        new StatModifiersModule().RegisterComponents(manager);
        new AbilityScoresModule().RegisterComponents(manager);
        return manager;
    }

    private static AbilityScoreComponent GetAbilityScore(ComponentManager manager, int entityId, AbilityScoreType type)
    {
        var pool = manager.GetMultiPool<AbilityScoreComponent>();
        for (var denseIndex = pool.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = pool.GetNextDenseIndex(denseIndex))
        {
            var component = pool.GetReadonlyByDenseIndex(denseIndex);
            if (component.Type == type)
            {
                return component;
            }
        }

        throw new InvalidOperationException($"No AbilityScoreComponent of type {type} for entity {entityId}.");
    }

    [TestMethod]
    public void Grant_NoModifiers_TotalEqualsBaseValue()
    {
        var manager = CreateRegisteredManager();

        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 7);

        var score = GetAbilityScore(manager, 0, AbilityScoreType.Strength);
        Assert.AreEqual((short)7, score.BaseValue);
        Assert.AreEqual((short)7, score.Total);
    }

    [TestMethod]
    public void Grant_BaseValueOutOfRange_Clamps()
    {
        var manager = CreateRegisteredManager();

        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 500);

        Assert.AreEqual((short)300, GetAbilityScore(manager, 0, AbilityScoreType.Strength).BaseValue);
    }

    [TestMethod]
    public void GrantDefaults_GrantsAllSevenTypesAtTheSameBaseValue()
    {
        var manager = CreateRegisteredManager();

        AbilityScoreEffects.GrantDefaults(manager, 0, 5);

        foreach (var type in Enum.GetValues<AbilityScoreType>())
        {
            Assert.AreEqual((short)5, GetAbilityScore(manager, 0, type).BaseValue);
        }
    }

    [TestMethod]
    public void GrantModifier_TargetingAbilityScore_RecomputesThatScoresTotalInline()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);

        AbilityScoreEffects.GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 3f, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);

        Assert.AreEqual((short)8, GetAbilityScore(manager, 0, AbilityScoreType.Strength).Total);
    }

    [TestMethod]
    public void GrantModifier_TargetingAbilityScore_DoesNotTouchOtherScores()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Dexterity, 5);

        AbilityScoreEffects.GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: 3f, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);

        Assert.AreEqual((short)5, GetAbilityScore(manager, 0, AbilityScoreType.Dexterity).Total);
    }

    [TestMethod]
    public void RecomputeIfAbilityScore_NonAbilityScoreTarget_IsANoOp()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        StatModifierEffects.Apply(manager, 0, StatModifierTarget.IncomingDamage, StatModifierOperation.Additive, StatModifierPolarity.Buff,
            canModify: true, magnitude: -1f, durationFrames: StatModifierComponent.Permanent, StatusEffectSource.Admin);

        AbilityScoreEffects.RecomputeIfAbilityScore(manager, 0, StatModifierTarget.IncomingDamage);

        var effectiveIncomingDamage = StatModifierMath.GetEffectiveValue(manager.GetMultiPool<StatModifierComponent>(), 0, StatModifierTarget.IncomingDamage, 10f);
        Assert.AreEqual(9f, effectiveIncomingDamage);
        Assert.AreEqual((short)5, GetAbilityScore(manager, 0, AbilityScoreType.Strength).Total);
    }

    [TestMethod]
    public void RecomputeIfAbilityScore_NoAbilityScoresModuleRegistered_DoesNotThrow()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        new StatModifiersModule().RegisterComponents(manager);

        AbilityScoreEffects.RecomputeIfAbilityScore(manager, 0, StatModifierTarget.Strength);
    }
}
