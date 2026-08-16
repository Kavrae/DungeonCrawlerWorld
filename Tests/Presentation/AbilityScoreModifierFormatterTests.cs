using Engine.ECS.Components;
using Game.Modules.AbilityScores;
using Game.Modules.Core;
using Game.Modules.Core.Components;
using Game.Modules.StatModifiers;
using Game.World;
using Presentation.UI.AbilityScores;

namespace Tests.Presentation;

[TestClass]
public sealed class AbilityScoreModifierFormatterTests
{
    private static ComponentManager CreateRegisteredManager()
    {
        var manager = new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8);
        new CoreModule().RegisterComponents(manager);
        new StatModifiersModule().RegisterComponents(manager);
        new AbilityScoresModule().RegisterComponents(manager);
        return manager;
    }

    private static void GrantModifier(ComponentManager manager, int entityId, AbilityScoreType type, StatModifierOperation operation, float magnitude, StatusEffectSource source) =>
        AbilityScoreEffects.GrantModifier(manager, entityId, type, operation, StatModifierPolarity.Buff,
            canModify: true, magnitude, durationFrames: null, source);

    [TestMethod]
    public void GetOrderedLines_NoModifiers_ReturnsOnlyUnsignedBaseLine()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 6);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        CollectionAssert.AreEqual(new[] { "Base : 6" }, lines.ToArray());
    }

    [TestMethod]
    public void GetOrderedLines_AdditiveOrderedBeforeMultiplicative()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Multiplicative, 0.5f, StatusEffectSource.Admin);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 2f, StatusEffectSource.AI);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        Assert.AreEqual("Base : 5", lines[0]);
        Assert.AreEqual("AI : +2", lines[1]);
        Assert.AreEqual("Admin : +50%", lines[2]);
    }

    [TestMethod]
    public void GetOrderedLines_PositiveBeforeNegative_WithinSameOperation()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, -1f, StatusEffectSource.AI);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 3f, StatusEffectSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        Assert.AreEqual("Admin : +3", lines[1]);
        Assert.AreEqual("AI : -1", lines[2]);
    }

    [TestMethod]
    public void GetOrderedLines_FullOrdering_FlatBeforeMultiplicative_PositiveBeforeNegative()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Multiplicative, -0.1f, StatusEffectSource.Admin);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, -1f, StatusEffectSource.AI);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Multiplicative, 0.25f, StatusEffectSource.Admin);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 2f, StatusEffectSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        CollectionAssert.AreEqual(new[] { "Base : 5", "Admin : +2", "AI : -1", "Admin : +25%", "Admin : -10%" }, lines.ToArray());
    }

    [TestMethod]
    public void GetOrderedLines_AdditiveMagnitude_FormatsAsRoundedSignedInteger()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 2.6f, StatusEffectSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        Assert.AreEqual("Admin : +3", lines[1]);
    }

    [TestMethod]
    public void GetOrderedLines_MultiplicativeMagnitude_FormatsAsRoundedSignedPercent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Multiplicative, -0.104f, StatusEffectSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        Assert.AreEqual("Admin : -10%", lines[1]);
    }

    [TestMethod]
    public void GetOrderedLines_EntitySourceWithDisplayText_UsesName()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        manager.Merge(1, new DisplayTextComponent("Iron Ring", "A plain iron ring."));
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 1f, StatusEffectSource.FromEntity(1));

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        Assert.AreEqual("Iron Ring : +1", lines[1]);
    }

    [TestMethod]
    public void GetOrderedLines_EntitySourceWithoutDisplayText_FallsBackToNumericLabel()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 1f, StatusEffectSource.FromEntity(7));

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        Assert.AreEqual("Entity#7 : +1", lines[1]);
    }

    [TestMethod]
    public void GetOrderedLines_ModifierTargetingDifferentAbilityScore_IsExcluded()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Dexterity, 4);
        GrantModifier(manager, 0, AbilityScoreType.Dexterity, StatModifierOperation.Additive, 9f, StatusEffectSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength);

        CollectionAssert.AreEqual(new[] { "Base : 5" }, lines.ToArray());
    }
}
