using Game.Modules.Actions;
using Presentation.UI;
using Presentation.UI.Details;

namespace Tests.Presentation;

/// <summary>How a compared definition's cooldown and cost lines are coloured -- lower is better and none is best, and a cost compares only with the same resource -- and how Dodge's cooldown and a windup read.</summary>
[TestClass]
public sealed class ComparisonLineStylesTests
{
    private static ComparisonLineStyles ComparedAgainst(params Guid[] actionIds)
    {
        var styles = new ComparisonLineStyles(EffectTextNames.None);
        foreach (var actionId in actionIds)
        {
            styles.Add(TestContent.Action(actionId), preview: null);
        }

        return styles;
    }

    private static ItemComparisonStat LineOf(Guid actionId, string keyPrefix) =>
        ItemComparisonStatExtraction.Extract(TestContent.Action(actionId), EffectTextNames.None).Single(stat => stat.Key.StartsWith(keyPrefix, StringComparison.Ordinal));

    [TestMethod]
    public void ACooldown_IsWorseThanNone_AndAShorterOneIsBetter()
    {
        var battleFuryCooldown = LineOf(BuiltInActionIds.BattleFury, "activator:cooldown");

        Assert.AreEqual(ActivatableDetailsSections.DebuffColor, ComparedAgainst(BuiltInActionIds.QuickAttack).Resolve(battleFuryCooldown).TextColor, "Quick Attack has no cooldown.");
        Assert.AreEqual(ActivatableDetailsSections.DebuffColor, ComparedAgainst(BuiltInActionIds.Lantern).Resolve(battleFuryCooldown).TextColor, "30s against Lantern's 1s.");
        Assert.AreEqual(ActivatableDetailsSections.BuffColor, ComparedAgainst(BuiltInActionIds.BattleFury).Resolve(LineOf(BuiltInActionIds.Lantern, "activator:cooldown")).TextColor);
    }

    [TestMethod]
    public void AManaCost_IsWorseThanNone_AndASmallerOneIsBetter()
    {
        var fireballMana = LineOf(BuiltInActionIds.Fireball, "cost:use:Mana");

        Assert.AreEqual(ActivatableDetailsSections.DebuffColor, ComparedAgainst(BuiltInActionIds.QuickAttack).Resolve(fireballMana).TextColor, "Quick Attack costs nothing.");
        Assert.AreEqual(ActivatableDetailsSections.DebuffColor, ComparedAgainst(BuiltInActionIds.Heal).Resolve(fireballMana).TextColor, "15 mana against Heal's 2.");
        Assert.AreEqual(ActivatableDetailsSections.BuffColor, ComparedAgainst(BuiltInActionIds.Fireball).Resolve(LineOf(BuiltInActionIds.Heal, "cost:use:Mana")).TextColor);
    }

    [TestMethod]
    public void CostsOfDifferentResources_AreNeverCompared_EachIsAgainstNoneOfItsOwnKind()
    {
        var health = new ItemComparisonStat("cost:use:Health", "To use: Drains 5 health", 5, HigherIsBetter: false, ValueWhenAbsent: 0);
        var styles = ComparedAgainst(BuiltInActionIds.Heal);

        Assert.AreEqual(ActivatableDetailsSections.DebuffColor, styles.Resolve(health).TextColor, "Heal drains mana, not health: none of this kind is better.");
        Assert.IsNull(styles.Resolve(health).Suffix);
    }

    [TestMethod]
    public void ADelayedActionsLock_ReadsAsItsWindup_AndComparesOnlyWithWindups()
    {
        var fireball = ItemComparisonStatExtraction.ExtractActivatorStats(TestContent.Action(BuiltInActionIds.Fireball).Activator).ToList();

        CollectionAssert.Contains(fireball.Select(stat => stat.DisplayText).ToList(), "Windup: 3s");
        Assert.IsFalse(fireball.Any(stat => stat.Key == "activator:actionLock"), "Its lock is its windup: nothing locks after it.");
    }

    [TestMethod]
    public void DodgesCooldown_IsTheUsersActionLock_WithItsReasonInAdvancedModesTooltip()
    {
        var dodge = TestContent.Action(BuiltInActionIds.Dodge).Activator;
        var preview = new Game.Views.ActivatablePreview(new Dictionary<Game.Effects.IEffectEntry, Game.Effects.EffectAmountPreview>(), dodge.Targeting, Range: null, AreaSize: null, ActionLockFrames: 30);

        var advanced = ItemComparisonStatExtraction.ExtractActivatorStats(dodge, preview, DetailsMode.Advanced).Single(stat => stat.Key == "activator:cooldown");
        var simple = ItemComparisonStatExtraction.ExtractActivatorStats(dodge, preview, DetailsMode.Simple).Single(stat => stat.Key == "activator:cooldown");

        Assert.AreEqual("Cooldown: 0.5s", advanced.DisplayText);
        Assert.AreEqual(30.0, advanced.ComparableValue);
        Assert.AreEqual(ItemComparisonStatExtraction.CooldownMatchesActionLockTooltip, advanced.TooltipText);
        Assert.IsNull(simple.TooltipText);
        StringAssert.DoesNotMatch(TestContent.Action(BuiltInActionIds.Dodge).Description, new System.Text.RegularExpressions.Regex("action lock"));
    }
}
