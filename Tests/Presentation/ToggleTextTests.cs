using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Inventory;
using Presentation.UI;
using Presentation.UI.Content;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>How a toggle is described in text: "Active" while it is on, what turning it on takes, and its upkeep.</summary>
[TestClass]
public sealed class ToggleTextTests
{
    private static readonly ToggleItemActivator LitActivator = new(new ActionTiming(ActionTimingCategory.Delayed), IsToggledOn: true);

    [TestMethod]
    public void ASpellThatIsNotAToggle_DescribesItsCostAsWhatUsingItTakes()
    {
        Assert.AreEqual("To use: Drains 15 mana", CostText.ActivationLine(TestContent.Action(BuiltInActionIds.Fireball), EffectTextNames.None));
        Assert.IsNull(CostText.UpkeepLine(TestContent.Action(BuiltInActionIds.Fireball), EffectTextNames.None));
    }

    [TestMethod]
    public void EveryCostAUseTakes_IsListed()
    {
        var action = TestContent.Action(BuiltInActionIds.Fireball) with { ActivationEffects = [new Effect([new ManaDrain(5), new HealthDrain(3)])] };

        Assert.AreEqual("To use: Drains 5 mana; Drains 3 health", CostText.ActivationLine(action, EffectTextNames.None));
    }

    [TestMethod]
    public void ToxicAura_DescribesItsUpkeep_AndNoTurnOnCost()
    {
        var action = TestContent.Action(BuiltInActionIds.ToxicAura);

        Assert.IsNull(CostText.ActivationLine(action, EffectTextNames.None));
        Assert.AreEqual("While on, every 1s: Drains 1 mana", CostText.UpkeepLine(action, EffectTextNames.None));
    }

    [TestMethod]
    public void ActivationEffects_AreDescribedAsWhatTurningOnTakes()
    {
        var item = TestContent.Item(BuiltInItemIds.ToxicIdol) with { ActivationEffects = [new Effect([new ManaDrain(5)])] };

        Assert.AreEqual("To turn on: Drains 5 mana", CostText.ActivationLine(item, EffectTextNames.None));
        Assert.IsNull(CostText.UpkeepLine(item, EffectTextNames.None));
    }

    [TestMethod]
    public void ItemHoverSummary_PutsActiveFirstForALitUnit_AndOnlyThen()
    {
        var unlit = TestContent.Item(BuiltInItemIds.ToxicIdol);
        var lit = unlit with { Activator = LitActivator };

        Assert.StartsWith(ToggleText.Active + "\n", ItemHoverSummary.For(lit, showCharges: true));
        Assert.DoesNotContain(ToggleText.Active, ItemHoverSummary.For(unlit, showCharges: true));
    }

    [TestMethod]
    public void ItemHoverSummary_ListsATogglesUpkeep()
    {
        var item = TestContent.Item(BuiltInItemIds.ToxicIdol) with { Toggle = new ToggleSpec(new TogglePeriodicEffects([new Effect([new ManaDrain(2)])], IntervalFrames: 30)) };

        Assert.Contains("While on, every 0.5s: Drains 2 mana", ItemHoverSummary.For(item, showCharges: true));
    }

    [TestMethod]
    public void AToggleHeldAura_IsNotDescribedAsPermanent()
    {
        var line = EffectFormatting.FormatEntry(TestContent.Item(BuiltInItemIds.ToxicIdol).Effects[0].Entries[0], EffectTextNames.None, DetailsMode.Simple);

        Assert.AreEqual("Grants a size 4 Poison aura with strength 16", line);
    }
}
