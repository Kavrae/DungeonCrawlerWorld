using Engine.ECS.Components;
using Engine.Tags;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Spawning;
using Game.Tags;

namespace Tests.GameTests.Modules.Actions;

[TestClass]
public sealed class ActionTagQueriesTests
{
    private const int EntityId = 0;

    private static List<(GameplayTag Tag, int Count)> CountsForACrawlerGranted(params Guid[] actionIds)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 10));
        var catalog = new ActionCatalog();
        foreach (var actionId in actionIds)
        {
            catalog.Register(TestContent.Action(actionId));
            componentManager.GetMultiPool<ActionInstanceComponent>().Add(EntityId, new ActionInstanceComponent(actionId, overrideDefinition: null));
        }

        componentManager.Merge(EntityId, new SpawnRecordComponent(0, SpawnFlags.Crawler, Seed: 1));
        var entityActions = EntityActions.For(componentManager, catalog, new BlueprintRegistry());
        return ActionTagQueries.GetActionCategoryCounts(entityActions, TestGameplayTags.BuiltIn, EntityId);
    }

    [TestMethod]
    public void Spells_CountUnderSpell_FromTheirActivatorsImpliedTag()
    {
        var counts = CountsForACrawlerGranted(BuiltInActionIds.Heal, BuiltInActionIds.Fireball);

        Assert.AreEqual(2, counts.Single(entry => entry.Tag == GameTags.Spell).Count);
    }

    [TestMethod]
    public void Attacks_CountUnderActionAttack_AndTheActionRootIsNeverACategory()
    {
        var counts = CountsForACrawlerGranted(BuiltInActionIds.Fireball, BuiltInActionIds.MagicMissile);

        Assert.AreEqual(2, counts.Single(entry => entry.Tag == GameTags.ActionAttack).Count);
        Assert.IsFalse(counts.Any(entry => entry.Tag == GameTags.Action));
        Assert.IsTrue(counts.All(entry => entry.Tag.IsSelfOrDescendantOf(GameTags.Action) || entry.Tag.IsSelfOrDescendantOf(GameTags.Spell)), $"Only Action.* and Spell categories expected, got {string.Join(", ", counts)}.");
    }

    [TestMethod]
    public void CoreActions_CountNowhere()
    {
        var counts = CountsForACrawlerGranted(BuiltInActionIds.PowerAttack, BuiltInActionIds.QuickAttack, BuiltInActionIds.Dodge);

        Assert.AreEqual(0, counts.Count, $"Core actions have a tab of their own, got {string.Join(", ", counts)}.");
    }

    [TestMethod]
    public void TheMostPopulatedCategory_ComesFirst()
    {
        var counts = CountsForACrawlerGranted(BuiltInActionIds.Heal, BuiltInActionIds.Lantern, BuiltInActionIds.Fireball);

        Assert.AreEqual(GameTags.Spell, counts[0].Tag);
    }
}
