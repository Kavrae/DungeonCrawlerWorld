using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Targeting;

namespace Tests.Presentation.Targeting;

/// <summary>Advanced mode adds a callout for each targeting rule that departs from the default, and only for those; each callout's text is a targeting stat.</summary>
[TestClass]
public sealed class TargetSchematicCalloutTests
{
    private const float Tolerance = 0.01f;

    private static readonly TargetSchematicMetrics Metrics = new(16f, new Vector2(30f, 12f));

    private static readonly TargetingSpec Fireball = new(TargetShape.Circle, 10, 3, Path: TargetPath.Projectile, ProjectileHitCount: 1,
        AimLineOfSight: LineOfSightRule.Required, ShapeLineOfSight: LineOfSightRule.Required);

    private static readonly TargetingSpec BouncingBolt = new(TargetShape.None, 8, Path: TargetPath.Projectile, PathEnd: ProjectilePathEnd.AtMaximumRange,
        ProjectileHitCount: TargetingSpec.AllEntitiesOnPath, PathWalls: PathWallRule.BouncesOffWalls, WallBounceCount: 1);

    private static readonly TargetingSpec Potion = new(TargetShape.Circle, 3, 1, TargetModeAffects: TargetModeAffects.MarkedOnly);

    private static TargetSchematic Advanced(TargetingSpec spec) => TargetSchematicPlanner.Plan(spec, Metrics, DetailsMode.Advanced);

    private static TargetSchematic Simple(TargetingSpec spec) => TargetSchematicPlanner.Plan(spec, Metrics, DetailsMode.Simple);

    private static IReadOnlyList<TargetSchematicLabel> LabelsOf(TargetSchematic schematic) => schematic.Labels.Select(static slot => slot.Label).ToList();

    [TestMethod]
    public void ASpecHoldingOnlyDefaults_IsDrawnTheSameInBothModes()
    {
        var bolt = new TargetingSpec(TargetShape.SingleTarget, 6);

        Assert.AreEqual(0, TargetSchematicPlanner.CalloutsOf(bolt).Count);
        Assert.AreEqual(Simple(bolt).Strokes.Count, Advanced(bolt).Strokes.Count);
        CollectionAssert.AreEqual(LabelsOf(Simple(bolt)).ToList(), LabelsOf(Advanced(bolt)).ToList());
    }

    [TestMethod]
    public void ADefaultCircle_HasNoCallouts_ItsRangeAndRadiusSayWhereItReaches()
    {
        var circle = new TargetingSpec(TargetShape.Circle, 5, 3);

        Assert.AreEqual(0, TargetSchematicPlanner.CalloutsOf(circle).Count);
        CollectionAssert.AreEquivalent(LabelsOf(Simple(circle)).ToList(), LabelsOf(Advanced(circle)).ToList());
    }

    [TestMethod]
    public void OnABrokenPath_TheHitMarkComesAfterTheBreak()
    {
        var schematic = Advanced(Fireball);

        var hitMark = schematic.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Path && stroke.Kind == TargetSchematicStrokeKind.Arc);
        var breakMark = schematic.Strokes.Single(static stroke => stroke.Kind == TargetSchematicStrokeKind.BreakMark);
        Assert.IsTrue(hitMark.Start.X > breakMark.Start.X);

        var unbroken = Advanced(Fireball with { MaximumRange = 6 });
        var unbrokenHit = unbroken.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Path && stroke.Kind == TargetSchematicStrokeKind.Arc);
        var aim = unbroken.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Aim);
        Assert.IsTrue(unbrokenHit.Start.X - unbroken.CasterPosition.X < (aim.Start.X - unbroken.CasterPosition.X) / 2f, "with no break, the hit mark stays in the path's first half");
    }

    [TestMethod]
    public void ACone_IsMeasuredAcrossItsFarEnd_AsWellAsAlongIt()
    {
        foreach (var anchor in new[] { TargetAnchor.Caster, TargetAnchor.PathEnd })
        {
            var cone = new TargetingSpec(TargetShape.Cone, 3, 4, Anchor: anchor);
            var schematic = Simple(cone);

            var width = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.ConeWidth);
            Assert.AreEqual(TargetSchematicLabelPlacement.RightOf, width.Placement);
            Assert.IsTrue(schematic.Labels.Any(static slot => slot.Label == TargetSchematicLabel.AreaSize));
        }
    }

    [TestMethod]
    public void ARing_ShowsItsInnerRadius_AndItsOuterRadius()
    {
        var ring = new TargetingSpec(TargetShape.Ring, 10, 3);
        var schematic = Simple(ring);

        var outer = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.AreaSize);
        var inner = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.InnerRadius);
        Assert.IsTrue(inner.Anchor.X < outer.Anchor.X, "the inner radius is called out on the left, the outer on the right");
    }

    [TestMethod]
    public void SimpleMode_DrawsNoCallouts()
    {
        foreach (var spec in new[] { Fireball, BouncingBolt, Potion })
        {
            Assert.IsTrue(LabelsOf(Simple(spec)).All(static label => label is TargetSchematicLabel.Range or TargetSchematicLabel.AreaSize));
            Assert.IsFalse(Simple(spec).Strokes.Any(static stroke => stroke.Role is TargetSchematicStrokeRole.Wall or TargetSchematicStrokeRole.FaintShape));
        }
    }

    [TestMethod]
    public void Fireball_CallsOutItsFirstHit_AndBothSightRules_WithItsOutlineDashed()
    {
        var schematic = Advanced(Fireball);

        CollectionAssert.AreEqual(
            new[] { TargetSchematicLabel.HitCount, TargetSchematicLabel.AimSight, TargetSchematicLabel.ShapeSight },
            TargetSchematicPlanner.CalloutsOf(Fireball).ToArray());
        CollectionAssert.IsSubsetOf(TargetSchematicPlanner.CalloutsOf(Fireball).ToList(), LabelsOf(schematic).ToList());
        Assert.IsTrue(schematic.Strokes.Where(static stroke => stroke.Role == TargetSchematicStrokeRole.Shape).All(static stroke => stroke.Pattern.Dash > 0f));
        Assert.AreEqual(1, schematic.Strokes.Count(static stroke => stroke.Role == TargetSchematicStrokeRole.Path && stroke.Kind == TargetSchematicStrokeKind.Arc), "the hit mark's circle");
    }

    [TestMethod]
    public void APotion_DrawsItsShapeFaint_AndSaysItHitsOneTarget()
    {
        var schematic = Advanced(Potion);

        Assert.IsFalse(schematic.Strokes.Any(static stroke => stroke.Role == TargetSchematicStrokeRole.Shape));
        Assert.IsTrue(schematic.Strokes.Any(static stroke => stroke.Role == TargetSchematicStrokeRole.FaintShape));
        Assert.Contains(TargetSchematicLabel.MarkedOnly, LabelsOf(schematic).ToList());
    }

    [TestMethod]
    public void ABouncingBolt_RunsOnPastItsAim_ToAnEndTick_AndFoldsBackOffAWall()
    {
        var schematic = Advanced(BouncingBolt);

        CollectionAssert.AreEquivalent(
            new[] { TargetSchematicLabel.Range, TargetSchematicLabel.HitCount, TargetSchematicLabel.PathEnd, TargetSchematicLabel.Walls },
            LabelsOf(schematic).ToList());
        var aim = schematic.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Aim);
        var pathTip = schematic.Strokes.First(static stroke => stroke.Role == TargetSchematicStrokeRole.Path && stroke.Kind == TargetSchematicStrokeKind.Arrowhead && stroke.StartRadians == 0f);
        Assert.IsTrue(aim.Start.X < pathTip.Start.X, "the path runs on past the aim");
        Assert.IsTrue(schematic.Strokes.Any(static stroke => stroke.Role == TargetSchematicStrokeRole.Wall && stroke.Kind == TargetSchematicStrokeKind.Hatch));
        Assert.AreEqual(2, schematic.Strokes.Count(static stroke => stroke.Role == TargetSchematicStrokeRole.Path && stroke.Kind == TargetSchematicStrokeKind.Arrowhead), "the path's arrow and the fold's");
    }

    [TestMethod]
    public void AMinimumRange_HatchesTheGroundItLeavesOut_AndIsMeasuredUnderTheRange()
    {
        var spec = new TargetingSpec(TargetShape.Circle, 10, 2, MinimumRange: 3);
        var schematic = Advanced(spec);

        var hatch = schematic.Strokes.Single(static stroke => stroke.Kind == TargetSchematicStrokeKind.Hatch);
        Assert.AreEqual(TargetSchematicStrokeRole.Wall, hatch.Role);
        Assert.IsTrue(hatch.Start.X > schematic.CasterPosition.X);
        var range = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.Range);
        var minimum = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.MinimumRange);
        Assert.IsTrue(minimum.Anchor.Y > range.Anchor.Y);
        Assert.IsTrue(minimum.Anchor.X < range.Anchor.X, "the minimum is nearer the caster");
    }

    [TestMethod]
    public void EveryAdvancedStrokeAndLabel_LiesInsideTheSchematic()
    {
        var specs = new[]
        {
            Fireball, BouncingBolt, Potion,
            Fireball with { Path = TargetPath.Lobbed, MinimumRange = 2 },
            new TargetingSpec(TargetShape.Wall, 10, 9, Path: TargetPath.Projectile, PathWalls: PathWallRule.PassesThroughWalls, ShapeLineOfSight: LineOfSightRule.Required),
            new TargetingSpec(TargetShape.Cone, 2, 4, Path: TargetPath.Projectile, ProjectileHitCount: 3, AimLineOfSight: LineOfSightRule.Required),
        };

        foreach (var spec in specs)
        {
            var schematic = Advanced(spec);
            foreach (var stroke in schematic.Strokes.Where(static stroke => stroke.Kind == TargetSchematicStrokeKind.Line))
            {
                AssertInside(schematic, stroke.Start, spec);
                AssertInside(schematic, stroke.End, spec);
            }

            foreach (var slot in schematic.Labels)
            {
                var topLeft = slot.TopLeftFor(Metrics.LabelSize);
                AssertInside(schematic, topLeft, spec);
                AssertInside(schematic, topLeft + Metrics.LabelSize, spec);
            }
        }
    }

    private static void AssertInside(TargetSchematic schematic, Vector2 point, TargetingSpec spec) =>
        Assert.IsTrue(point.X >= -Tolerance && point.Y >= -Tolerance && point.X <= schematic.Size.X + Tolerance && point.Y <= schematic.Size.Y + Tolerance, $"{point} outside {schematic.Size} for {spec.Shape} {spec.Path}");

    [TestMethod]
    public void EveryCallout_HasItsStat_OnlyInAdvancedMode()
    {
        foreach (var spec in new[] { Fireball, BouncingBolt, Potion, new TargetingSpec(TargetShape.Circle, 10, 2, MinimumRange: 3) })
        {
            var activator = new SpellActivator(spec, new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null));
            var advancedKeys = ItemComparisonStatExtraction.ExtractTargetingStats(activator, mode: DetailsMode.Advanced).Select(static stat => stat.Key).ToList();
            var simpleKeys = ItemComparisonStatExtraction.ExtractTargetingStats(activator, mode: DetailsMode.Simple).Select(static stat => stat.Key).ToList();

            foreach (var label in LabelsOf(Advanced(spec)))
            {
                Assert.Contains(ItemComparisonStatExtraction.TargetingKeyOf(label), advancedKeys, $"{label} for {spec.Shape}");
            }

            foreach (var callout in TargetSchematicPlanner.CalloutsOf(spec))
            {
                Assert.DoesNotContain(ItemComparisonStatExtraction.TargetingKeyOf(callout), simpleKeys);
            }
        }
    }

    [TestMethod]
    public void CalloutTexts_SayWhatTheRuleIs()
    {
        Assert.AreEqual("hits 1", CalloutText(Fireball, TargetSchematicLabel.HitCount));
        Assert.AreEqual("Required LoS", CalloutText(Fireball, TargetSchematicLabel.AimSight));
        Assert.AreEqual("Line of Sight to the targeted tile is required.", Stat(Fireball, TargetSchematicLabel.AimSight).TooltipText);
        Assert.AreEqual("Blocked by walls", CalloutText(Fireball, TargetSchematicLabel.ShapeSight));
        Assert.AreEqual("Set distance", CalloutText(BouncingBolt, TargetSchematicLabel.PathEnd));
        Assert.AreEqual("Hits every target in its path", Stat(BouncingBolt, TargetSchematicLabel.HitCount).TooltipText);
        Assert.AreEqual("Splash 1", Stat(Potion, TargetSchematicLabel.AreaSize).DisplayText);
        Assert.AreEqual("r 3", Stat(Fireball, TargetSchematicLabel.AreaSize).DisplayText);
        Assert.AreEqual("r 2", Stat(new TargetingSpec(TargetShape.Ring, 10, 3), TargetSchematicLabel.InnerRadius).DisplayText);
        Assert.AreEqual("4", Stat(new TargetingSpec(TargetShape.Cone, 0, 4, Anchor: TargetAnchor.Caster), TargetSchematicLabel.ConeWidth).DisplayText);
        Assert.AreEqual("hits 2", CalloutText(Fireball with { ProjectileHitCount = 2 }, TargetSchematicLabel.HitCount));
        Assert.AreEqual("hits none", CalloutText(Fireball with { ProjectileHitCount = 0 }, TargetSchematicLabel.HitCount));
        Assert.AreEqual("hits all", CalloutText(BouncingBolt, TargetSchematicLabel.HitCount));
        Assert.AreEqual("bounces ×1", CalloutText(BouncingBolt, TargetSchematicLabel.Walls));
        Assert.AreEqual("through walls", CalloutText(BouncingBolt with { PathWalls = PathWallRule.PassesThroughWalls }, TargetSchematicLabel.Walls));
        Assert.AreEqual("one target", CalloutText(Potion, TargetSchematicLabel.MarkedOnly));
        Assert.AreEqual("min 3", CalloutText(new TargetingSpec(TargetShape.Circle, 10, 2, MinimumRange: 3), TargetSchematicLabel.MinimumRange));
    }

    private static string CalloutText(TargetingSpec spec, TargetSchematicLabel callout) => Stat(spec, callout).DisplayText;

    private static ItemComparisonStat Stat(TargetingSpec spec, TargetSchematicLabel label)
    {
        var activator = new SpellActivator(spec, new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null));
        return ItemComparisonStatExtraction.ExtractTargetingStats(activator, mode: DetailsMode.Advanced).Single(stat => stat.Key == ItemComparisonStatExtraction.TargetingKeyOf(label));
    }
}
