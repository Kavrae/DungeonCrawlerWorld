using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Presentation.UI;
using Presentation.UI.Details;

namespace Tests.Presentation.Targeting;

/// <summary>The target schematic's labels are targeting stats: one per dimension the schematic draws, named in their tooltips, and compared like any line.</summary>
[TestClass]
public sealed class TargetingStatsTests
{
    private static ActionDefinition Fireball => TestContent.Action(BuiltInActionIds.Fireball);

    private static ActionDefinition FireballWith(TargetingSpec targeting) =>
        Fireball with { Activator = new SpellActivator(targeting, Fireball.Activator.Timing) };

    [TestMethod]
    public void Fireball_LabelsItsRange_AndItsRadius_EachNamedInItsTooltip()
    {
        var stats = ItemComparisonStatExtraction.ExtractTargetingStats(Fireball.Activator, mode: DetailsMode.Simple);

        var range = stats.Single(static stat => stat.Key == ItemComparisonStatExtraction.TargetingRangeKey);
        Assert.AreEqual("10", range.DisplayText);
        Assert.AreEqual("Range: 10", range.TooltipText);
        Assert.AreEqual(10.0, range.ComparableValue);

        var radius = stats.Single(static stat => stat.Key == ItemComparisonStatExtraction.TargetingAreaSizeKey);
        Assert.AreEqual("r 3", radius.DisplayText);
        Assert.AreEqual("Circle radius: 3", radius.TooltipText);
    }

    [TestMethod]
    public void AShapeAtTheCaster_HasNoRangeLabel_AndALengthIsNoRadius()
    {
        var cone = new SpellActivator(new TargetingSpec(TargetShape.Cone, 0, 4, Anchor: TargetAnchor.Caster), Fireball.Activator.Timing);

        var length = ItemComparisonStatExtraction.ExtractTargetingStats(cone).Single(static stat => stat.Key == ItemComparisonStatExtraction.TargetingAreaSizeKey);

        Assert.AreEqual(ItemComparisonStatExtraction.TargetingAreaSizeKey, length.Key);
        Assert.AreEqual("4", length.DisplayText);
        Assert.AreEqual("Cone length: 4", length.TooltipText);
    }

    [TestMethod]
    public void AShapeWithNoSize_HasNoAreaLabel()
    {
        var bolt = new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, 6), Fireball.Activator.Timing);

        Assert.AreEqual(ItemComparisonStatExtraction.TargetingRangeKey, ItemComparisonStatExtraction.ExtractTargetingStats(bolt).Single().Key);
    }

    [TestMethod]
    public void TargetingIsNeverAnActivatorLine()
    {
        var lines = ItemComparisonStatExtraction.ExtractActivatorStats(Fireball.Activator, mode: DetailsMode.Advanced);

        Assert.IsFalse(lines.Any(static stat => stat.Key is ItemComparisonStatExtraction.TargetingRangeKey or ItemComparisonStatExtraction.TargetingAreaSizeKey or "activator:shape"));
    }

    [TestMethod]
    public void ALongerRange_IsBetter_WhateverTheShapesCompared()
    {
        var styles = new ComparisonLineStyles(EffectTextNames.None);
        styles.Add(FireballWith(new TargetingSpec(TargetShape.SingleTarget, 6)), preview: null);

        var range = ItemComparisonStatExtraction.ExtractTargetingStats(Fireball.Activator).Single(static stat => stat.Key == ItemComparisonStatExtraction.TargetingRangeKey);

        Assert.AreEqual(ActivatableDetailsSections.BuffColor, styles.Resolve(range).TextColor);
    }

    [TestMethod]
    public void ASmallerRadius_IsWorse()
    {
        var styles = new ComparisonLineStyles(EffectTextNames.None);
        styles.Add(FireballWith(Fireball.Activator.Targeting with { AreaSize = 5 }), preview: null);

        var radius = ItemComparisonStatExtraction.ExtractTargetingStats(Fireball.Activator).Single(static stat => stat.Key == ItemComparisonStatExtraction.TargetingAreaSizeKey);

        Assert.AreEqual(ActivatableDetailsSections.DebuffColor, styles.Resolve(radius).TextColor);
    }
}
