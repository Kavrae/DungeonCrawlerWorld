using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Details;
using Presentation.UI.Targeting;

namespace Tests.Presentation.Targeting;

/// <summary>An aura's reach is a shaded diamond with its radius: where an activation places it, in that activation's schematic; where something holds it, around the holder's station.</summary>
[TestClass]
public sealed class TargetSchematicAuraTests
{
    private const float Tolerance = 0.01f;

    private static readonly TargetSchematicMetrics Metrics = new(16f, new Vector2(30f, 12f));

    private static ActionDefinition Torch => TestContent.Action(BuiltInActionIds.Torch);

    private static AuraDefinition TorchLight => Torch.Effects.SelectMany(static effect => effect.Entries).OfType<AuraSourceGrant>().Single().Aura;

    private static List<TargetSchematicStroke> BandsOf(TargetSchematic schematic) =>
        schematic.Strokes.Where(static stroke => stroke.Role == TargetSchematicStrokeRole.AuraBand).ToList();

    [TestMethod]
    public void ALinearAura_IsNeverShadedStrongerOutward()
    {
        var aura = new TargetSchematicAura(TorchLight with { Falloff = AuraFalloff.Linear }, 16, 4);
        var bands = BandsOf(TargetSchematicPlanner.PlanAuraReachToFit(aura, Metrics.LabelSize, new Vector2(400f, 200f)));

        Assert.AreEqual(5, bands.Count, "one band per distance, 0 to 4");
        for (var index = 1; index < bands.Count; index++)
        {
            Assert.IsTrue(bands[index].Radius < bands[index - 1].Radius, "outermost first");
            Assert.IsTrue(Distance(bands[index].Fill, TorchLight.GlowColor) <= Distance(bands[index - 1].Fill, TorchLight.GlowColor), "nearer the source, nearer the glow");
        }
    }

    [TestMethod]
    public void AnAuraWithNoFalloff_IsOneFlatBand()
    {
        var aura = new TargetSchematicAura(TorchLight with { Falloff = AuraFalloff.None }, 1, 4);

        var band = BandsOf(TargetSchematicPlanner.PlanAuraReachToFit(aura, Metrics.LabelSize, new Vector2(400f, 200f))).Single();

        Assert.AreEqual(TargetSchematicPlanner.AuraBandColor(aura, 1f), band.Fill);
    }

    [TestMethod]
    public void ALargeAura_SamplesItsDistancesEvenly_KeepingItsEdgeExact()
    {
        var aura = new TargetSchematicAura(TorchLight with { Falloff = AuraFalloff.Linear }, 20, 12);

        var bands = BandsOf(TargetSchematicPlanner.PlanAuraReachToFit(aura, Metrics.LabelSize, new Vector2(400f, 200f)));

        Assert.IsTrue(bands.Count < aura.Size + 1);
        var edgeStrength = AuraFalloff.Linear.ValueAt(aura.Power, aura.Size, aura.Size) / (float)aura.Power;
        Assert.AreEqual(TargetSchematicPlanner.AuraBandColor(aura, edgeStrength), bands[0].Fill);
        Assert.AreEqual(TargetSchematicPlanner.AuraBandColor(aura, 1f), bands[^1].Fill);
    }

    [TestMethod]
    public void AHeldAura_CentresOnItsHoldersStation_AndIsMeasuredFromIt()
    {
        var aura = new TargetSchematicAura(TorchLight, 8, 3);
        var schematic = TargetSchematicPlanner.PlanAuraReachToFit(aura, Metrics.LabelSize, new Vector2(400f, 200f));

        Assert.IsTrue(BandsOf(schematic).All(band => Vector2.Distance(band.Start, schematic.CasterPosition) < Tolerance));
        Assert.AreEqual(TargetSchematicLabel.AuraRadius, schematic.Labels.Single().Label);
        var station = schematic.Strokes.ToList().FindIndex(static stroke => stroke.Role == TargetSchematicStrokeRole.Caster);
        Assert.IsTrue(schematic.Strokes.Select((stroke, index) => (stroke, index)).Where(static pair => pair.stroke.Role == TargetSchematicStrokeRole.AuraBand).All(pair => pair.index < station), "the station is drawn over the aura");
    }

    [TestMethod]
    public void APlacedAura_IsDrawnWhereTheActivationLands_InOneSchematic_WithItsRangeAndRadius()
    {
        var spec = Torch.Activator.Targeting;
        var aura = ActivatableDetailsSections.AurasPlacedBy(Torch, preview: null).Single();

        var schematic = TargetSchematicPlanner.Plan(spec, Metrics, DetailsMode.Simple, [aura]);

        var aim = schematic.Strokes.Single(static stroke => stroke.Role == TargetSchematicStrokeRole.Aim);
        Assert.IsTrue(BandsOf(schematic).All(band => Vector2.Distance(band.Start, aim.Start) < Tolerance), "centred on the aim");
        CollectionAssert.AreEquivalent(new[] { TargetSchematicLabel.Range, TargetSchematicLabel.AuraRadius }, schematic.Labels.Select(static slot => slot.Label).ToList());
        var range = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.Range);
        var radius = schematic.Labels.Single(static slot => slot.Label == TargetSchematicLabel.AuraRadius);
        Assert.IsTrue(radius.Anchor.Y > range.Anchor.Y && radius.Anchor.X > range.Anchor.X, "the radius is measured on its own row, out from the aim");
        var path = schematic.Strokes.Where(static stroke => stroke.Role == TargetSchematicStrokeRole.Path && stroke.Kind == TargetSchematicStrokeKind.Line).Max(static stroke => stroke.End.X);
        Assert.IsTrue(path < aim.Start.X - TargetSchematicBands.ShapeUnits(aura.Size) * Metrics.Unit, "the path stops at the diamond's edge");
    }

    [TestMethod]
    public void OnlyAnActivationThatIsUsed_PlacesItsAuras_AToggleHoldsThem()
    {
        Assert.AreEqual(1, ActivatableDetailsSections.AurasPlacedBy(Torch, preview: null).Count);
        Assert.AreEqual(0, ActivatableDetailsSections.AurasPlacedBy(TestContent.Action(BuiltInActionIds.ToxicAura), preview: null).Count);
    }

    private static float Distance(Color first, Color second) =>
        Vector3.Distance(first.ToVector3(), second.ToVector3());
}
