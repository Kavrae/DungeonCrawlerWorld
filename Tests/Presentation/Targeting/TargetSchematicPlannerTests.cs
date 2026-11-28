using Engine.Tiles;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Targeting;

namespace Tests.Presentation.Targeting;

/// <summary>TargetSchematicPlanner draws every shape, keeps distances in order without scale, and puts each number on its dimension.</summary>
[TestClass]
public sealed class TargetSchematicPlannerTests
{
    private const float Tolerance = 0.01f;

    private static readonly TargetSchematicMetrics Metrics = new(16f, new Vector2(30f, 12f));

    private static readonly TargetingSpec Fireball = new(TargetShape.Circle, 10, 3, Path: TargetPath.Projectile, ProjectileHitCount: 1,
        AimLineOfSight: LineOfSightRule.Required, ShapeLineOfSight: LineOfSightRule.Required);

    private static IEnumerable<TargetShape> SingleShapes() =>
        Enum.GetValues<TargetShape>().Where(static shape => shape != TargetShape.None);

    [TestMethod]
    public void EveryShape_AndTheCombinationsContentUses_GiveAGlyph_AtTheAimAndAtTheCaster()
    {
        var shapes = SingleShapes().Concat([TargetShape.Adjacent | TargetShape.Self, TargetShape.Adjacent | TargetShape.SingleTarget]);
        foreach (var shape in shapes)
        {
            foreach (var anchor in new[] { TargetAnchor.PathEnd, TargetAnchor.Caster })
            {
                var schematic = TargetSchematicPlanner.Plan(new TargetingSpec(shape, 5, 3, Anchor: anchor), Metrics);

                Assert.IsTrue(schematic.Strokes.Any(static stroke => stroke.Role == TargetSchematicStrokeRole.Shape), $"{shape} at {anchor}");
            }
        }
    }

    [TestMethod]
    public void ACombinedShape_DrawsEachPart()
    {
        var ringAlone = ShapeStrokeCount(TargetShape.Adjacent);
        var selfAlone = ShapeStrokeCount(TargetShape.Self);

        Assert.AreEqual(ringAlone + selfAlone, ShapeStrokeCount(TargetShape.Adjacent | TargetShape.Self));
    }

    private static int ShapeStrokeCount(TargetShape shape) =>
        TargetSchematicPlanner.Plan(new TargetingSpec(shape, 0, Anchor: TargetAnchor.Caster), Metrics).Strokes.Count(static stroke => stroke.Role == TargetSchematicStrokeRole.Shape);

    [TestMethod]
    public void Bands_NeverDrawALongerDistanceShorter()
    {
        for (var distance = 1; distance <= 30; distance++)
        {
            Assert.IsTrue(TargetSchematicBands.PathUnits(distance) >= TargetSchematicBands.PathUnits(distance - 1), $"path {distance}");
            Assert.IsTrue(TargetSchematicBands.ShapeUnits(distance) >= TargetSchematicBands.ShapeUnits(distance - 1), $"shape {distance}");
        }
    }

    [TestMethod]
    public void APlannedPath_NeverDrawsALongerRangeShorter()
    {
        var previousDistance = 0f;
        for (var range = 1; range <= 30; range++)
        {
            var distance = AimDistance(TargetSchematicPlanner.Plan(Fireball with { MaximumRange = range }, Metrics));

            Assert.IsTrue(distance >= previousDistance - Tolerance, $"range {range}");
            previousDistance = distance;
        }
    }

    [TestMethod]
    public void WithRoomToSpare_APathIsStretchedToThreeTimesItsBand_AtTheLargestUnit()
    {
        var schematic = TargetSchematicPlanner.PlanToFit(Fireball, DetailsMode.Simple, Metrics.LabelSize, null, new Vector2(1000f, 200f));

        Assert.AreEqual(TargetSchematicBands.PathUnits(10) * TargetSchematicPlanner.LargestUnit * TargetSchematicMetrics.MaximumPathStretch, AimDistance(schematic), Tolerance);
    }

    [TestMethod]
    public void WithLittleRoom_APathStretchesLess_BeforeTheUnitShrinks()
    {
        var unstretched = TargetSchematicPlanner.Plan(Fireball, new TargetSchematicMetrics(TargetSchematicPlanner.LargestUnit, Metrics.LabelSize));
        var stretchedOnce = TargetSchematicPlanner.Plan(Fireball, new TargetSchematicMetrics(TargetSchematicPlanner.LargestUnit, Metrics.LabelSize, 1.25f));

        var schematic = TargetSchematicPlanner.PlanToFit(Fireball, DetailsMode.Simple, Metrics.LabelSize, null, new Vector2(stretchedOnce.Size.X, 200f));

        Assert.AreEqual(AimDistance(stretchedOnce), AimDistance(schematic), Tolerance);
        Assert.IsTrue(AimDistance(schematic) > AimDistance(unstretched));
    }

    [TestMethod]
    public void AShapeAtTheCaster_IsNeverStretched()
    {
        var cone = new TargetingSpec(TargetShape.Cone, 0, 4, Anchor: TargetAnchor.Caster);

        var schematic = TargetSchematicPlanner.PlanToFit(cone, DetailsMode.Simple, Metrics.LabelSize, null, new Vector2(1000f, 200f));

        Assert.AreEqual(TargetSchematicPlanner.Plan(cone, new TargetSchematicMetrics(TargetSchematicPlanner.LargestUnit, Metrics.LabelSize)).Size, schematic.Size);
    }

    [TestMethod]
    public void OnlyARangeFromTenOn_IsDrawnBroken()
    {
        Assert.IsFalse(HasBreakMark(TargetSchematicPlanner.Plan(Fireball with { MaximumRange = 9 }, Metrics)));
        Assert.IsTrue(HasBreakMark(TargetSchematicPlanner.Plan(Fireball with { MaximumRange = 10 }, Metrics)));
    }

    [TestMethod]
    public void AShapeAtTheCaster_HasNoPathAndNoRange_ButItsSize()
    {
        var schematic = TargetSchematicPlanner.Plan(new TargetingSpec(TargetShape.Cone, 0, 4, Anchor: TargetAnchor.Caster), Metrics);

        Assert.IsFalse(schematic.Strokes.Any(static stroke => stroke.Role is TargetSchematicStrokeRole.Path or TargetSchematicStrokeRole.Aim));
        Assert.IsFalse(schematic.Labels.Any(static slot => slot.Label == TargetSchematicLabel.Range));
        Assert.IsTrue(schematic.Labels.Any(static slot => slot.Label == TargetSchematicLabel.AreaSize));
    }

    [TestMethod]
    public void AShapeAimedAtNoRange_IsCentredOnTheCaster()
    {
        var schematic = TargetSchematicPlanner.Plan(new TargetingSpec(TargetShape.Circle, 0, 3), Metrics);

        var circle = schematic.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Shape);
        Assert.AreEqual(schematic.CasterPosition, circle.Start);
        Assert.IsFalse(schematic.Labels.Any(static slot => slot.Label == TargetSchematicLabel.Range));
    }

    [TestMethod]
    public void Fireball_IsTheCaster_ABrokenProjectile_AndACircleAtTheAim_WithItsRangeAndRadius()
    {
        var schematic = TargetSchematicPlanner.Plan(Fireball, Metrics);
        var caster = schematic.CasterPosition;
        var aim = schematic.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Aim);

        Assert.AreEqual(TargetSchematicStrokeKind.FillCircle, schematic.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Caster).Kind);
        Assert.AreEqual(TargetSchematicStrokeKind.CentreMark, aim.Kind);
        Assert.AreEqual(TargetSchematicBands.PathUnits(10) * Metrics.Unit, aim.Start.X - caster.X, Tolerance);
        Assert.AreEqual(caster.Y, aim.Start.Y, Tolerance);

        var path = schematic.Strokes.Where(static stroke => stroke.Role == TargetSchematicStrokeRole.Path).ToList();
        Assert.AreEqual(2, path.Count(static stroke => stroke.Kind == TargetSchematicStrokeKind.Line && stroke.Pattern.Dash == 0f));
        Assert.AreEqual(1, path.Count(static stroke => stroke.Kind == TargetSchematicStrokeKind.BreakMark));
        var arrowhead = path.Single(static stroke => stroke.Kind == TargetSchematicStrokeKind.Arrowhead);
        Assert.IsTrue(arrowhead.Start.X < aim.Start.X - TargetSchematicBands.ShapeUnits(3) * Metrics.Unit);

        var circle = schematic.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Shape);
        Assert.AreEqual(TargetSchematicStrokeKind.Arc, circle.Kind);
        Assert.AreEqual(aim.Start, circle.Start);
        Assert.AreEqual(TargetSchematicBands.ShapeUnits(3) * Metrics.Unit, circle.Radius, Tolerance);
        Assert.AreEqual(MathHelper.TwoPi, circle.SweepRadians, Tolerance);
        Assert.AreEqual(2, schematic.Strokes.Count(static stroke => stroke.Role == TargetSchematicStrokeRole.CentreLine));

        var range = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.Range);
        Assert.AreEqual(TargetSchematicLabelPlacement.AboveCentred, range.Placement);
        Assert.AreEqual((caster.X + aim.Start.X) / 2f, range.Anchor.X, Tolerance);
        Assert.IsTrue(range.Anchor.Y > circle.Start.Y + circle.Radius, "the range sits below the circle");

        var radius = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.AreaSize);
        Assert.AreEqual(TargetSchematicLabelPlacement.AboveStart, radius.Placement);
        Assert.IsTrue(radius.Anchor.X > aim.Start.X && radius.Anchor.Y < aim.Start.Y, "the radius leader comes from up and to the right");
    }

    [TestMethod]
    public void APlacedPath_IsDottedWithNoArrowhead_AndALobbedOneIsAnArc()
    {
        var placed = TargetSchematicPlanner.Plan(new TargetingSpec(TargetShape.SingleTarget, 6), Metrics);
        var lobbed = TargetSchematicPlanner.Plan(Fireball with { Path = TargetPath.Lobbed }, Metrics);

        Assert.IsTrue(placed.Strokes.Where(static stroke => stroke.Role == TargetSchematicStrokeRole.Path).All(static stroke => stroke.Kind == TargetSchematicStrokeKind.Line && stroke.Pattern.Dash > 0f));
        Assert.IsTrue(lobbed.Strokes.Any(static stroke => stroke.Role == TargetSchematicStrokeRole.Path && stroke.Kind == TargetSchematicStrokeKind.Arc));
        Assert.IsTrue(lobbed.Strokes.Any(static stroke => stroke.Role == TargetSchematicStrokeRole.Path && stroke.Kind == TargetSchematicStrokeKind.Arrowhead));
    }

    [TestMethod]
    public void AWall_IsMeasuredAcross_BesideIt()
    {
        var schematic = TargetSchematicPlanner.Plan(new TargetingSpec(TargetShape.Wall, 10, 9), Metrics);

        Assert.AreEqual(TargetSchematicLabelPlacement.RightOf, schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.AreaSize).Placement);
    }

    [TestMethod]
    public void EveryStrokeAndLabel_LiesInsideTheSchematic()
    {
        var specs = SingleShapes().SelectMany(static shape => new[]
        {
            new TargetingSpec(shape, 12, 6, Path: TargetPath.Lobbed),
            new TargetingSpec(shape, 3, 2, Path: TargetPath.Projectile),
            new TargetingSpec(shape, 0, 4, Anchor: TargetAnchor.Caster),
        });

        foreach (var spec in specs)
        {
            var schematic = TargetSchematicPlanner.Plan(spec, Metrics);
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
        Assert.IsTrue(point.X >= -Tolerance && point.Y >= -Tolerance && point.X <= schematic.Size.X + Tolerance && point.Y <= schematic.Size.Y + Tolerance, $"{point} outside {schematic.Size} for {spec.Shape}");

    private static float AimDistance(TargetSchematic schematic) =>
        schematic.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Aim).Start.X - schematic.CasterPosition.X;

    private static bool HasBreakMark(TargetSchematic schematic) =>
        schematic.Strokes.Any(static stroke => stroke.Kind == TargetSchematicStrokeKind.BreakMark);
}
