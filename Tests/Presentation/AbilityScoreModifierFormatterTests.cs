using Game.Blueprints;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
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
        var manager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));
        return manager;
    }

    private static void GrantModifier(ComponentManager manager, int entityId, AbilityScoreType type, StatModifierOperation operation, float magnitude, ActionSource source) =>
        AbilityScoreEffects.GrantModifier(manager, entityId, type, operation, StatModifierPolarity.Buff,
            canModify: true, magnitude, expiresAtFrame: FrameDeadline.Never, source);

    [TestMethod]
    public void GetOrderedLines_NoModifiers_ReturnsOnlyUnsignedBaseLine()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 6);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        CollectionAssert.AreEqual(new[] { "Base : 6" }, lines.Select(static line => line.Text).ToArray());
    }

    [TestMethod]
    public void GetOrderedLines_AdditiveOrderedBeforeMultiplicative()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Multiplicative, 0.5f, ActionSource.Admin);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 2f, ActionSource.AI);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        Assert.AreEqual("Base : 5", lines[0].Text);
        Assert.AreEqual("AI : +2", lines[1].Text);
        Assert.AreEqual("Admin : +50%", lines[2].Text);
    }

    [TestMethod]
    public void GetOrderedLines_PositiveBeforeNegative_WithinSameOperation()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, -1f, ActionSource.AI);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 3f, ActionSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        Assert.AreEqual("Admin : +3", lines[1].Text);
        Assert.AreEqual("AI : -1", lines[2].Text);
    }

    [TestMethod]
    public void GetOrderedLines_FullOrdering_FlatBeforeMultiplicative_PositiveBeforeNegative()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Multiplicative, -0.1f, ActionSource.Admin);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, -1f, ActionSource.AI);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Multiplicative, 0.25f, ActionSource.Admin);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 2f, ActionSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        CollectionAssert.AreEqual(new[] { "Base : 5", "Admin : +2", "AI : -1", "Admin : +25%", "Admin : -10%" }, lines.Select(static line => line.Text).ToArray());
    }

    [TestMethod]
    public void GetOrderedLines_AdditiveMagnitude_FormatsAsRoundedSignedInteger()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 2.6f, ActionSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        Assert.AreEqual("Admin : +3", lines[1].Text);
    }

    [TestMethod]
    public void GetOrderedLines_MultiplicativeMagnitude_FormatsAsRoundedSignedPercent()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Multiplicative, -0.104f, ActionSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        Assert.AreEqual("Admin : -10%", lines[1].Text);
    }

    [TestMethod]
    public void GetOrderedLines_EntitySourceWithDisplayText_UsesName()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        manager.Merge(1, new DisplayTextComponent("Iron Ring", "A plain iron ring."));
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 1f, ActionSource.FromEntity(manager, new EntityKeys(), 1, creatures: new BlueprintRegistry()));

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        Assert.AreEqual("Iron Ring : +1", lines[1].Text);
    }

    /// <summary>The name is recorded when the modifier is granted, so it still shows once the source entity is gone and its id belongs to someone else.</summary>
    [TestMethod]
    public void GetOrderedLines_SourceEntityDestroyedAndIdReused_StillShowsTheOriginalNameAndCrawlerNumber()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        manager.Merge(1, new DisplayTextComponent("Iron Ring", "A plain iron ring."));
        manager.Merge(1, new Game.Modules.Crawler.Components.CrawlerComponent(4242));
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 1f, ActionSource.FromEntity(manager, new EntityKeys(), 1, creatures: new BlueprintRegistry()));

        manager.RemoveAllComponents(1);
        manager.Merge(1, new DisplayTextComponent("Goblin", "Someone else."));

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        Assert.AreEqual("Iron Ring (Crawler #4242) : +1", lines[1].Text);
    }

    [TestMethod]
    public void GetOrderedLines_EntitySourceWithoutDisplayText_IsUnknown()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        GrantModifier(manager, 0, AbilityScoreType.Strength, StatModifierOperation.Additive, 1f, TestSources.Entity(7));

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        Assert.AreEqual("Unknown : +1", lines[1].Text);
    }

    [TestMethod]
    public void GetOrderedLines_ModifierTargetingDifferentAbilityScore_IsExcluded()
    {
        var manager = CreateRegisteredManager();
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Strength, 5);
        AbilityScoreEffects.Grant(manager, 0, AbilityScoreType.Dexterity, 4);
        GrantModifier(manager, 0, AbilityScoreType.Dexterity, StatModifierOperation.Additive, 9f, ActionSource.Admin);

        var lines = AbilityScoreModifierFormatter.GetOrderedLines(manager, 0, AbilityScoreType.Strength, now: 0);

        CollectionAssert.AreEqual(new[] { "Base : 5" }, lines.Select(static line => line.Text).ToArray());
    }
}
