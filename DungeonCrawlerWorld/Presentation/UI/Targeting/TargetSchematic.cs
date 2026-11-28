using Game.Modules.Auras;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace Presentation.UI.Targeting;

/// <summary>A planned target schematic: its strokes and label slots, in its own pixels from its top-left corner.</summary>
/// <param name="Size">The whole drawing's size, labels' reserved boxes included.</param>
/// <param name="CasterPosition">The centre of the caster's station.</param>
public sealed record TargetSchematic(Vector2 Size, Vector2 CasterPosition, IReadOnlyList<TargetSchematicStroke> Strokes, IReadOnlyList<TargetSchematicLabelSlot> Labels);

/// <summary>What a stroke is, which says how StrokeRenderer draws it.</summary>
public enum TargetSchematicStrokeKind : byte
{
    /// <summary>Start to End, patterned when Pattern has a dash.</summary>
    Line,

    /// <summary>An arc about Start of Radius, from StartRadians sweeping SweepRadians.</summary>
    Arc,

    /// <summary>A disc about Start of Radius.</summary>
    FillCircle,

    /// <summary>An open arrowhead with its tip at Start, pointing along StartRadians, its strokes Radius long.</summary>
    Arrowhead,

    /// <summary>A plus at Start, each arm Radius long.</summary>
    CentreMark,

    /// <summary>Two slashes across a line running along StartRadians, centred on Start, Radius their half-length.</summary>
    BreakMark,

    /// <summary>Diagonal hatching over the rectangle at Start of size End, its lines Radius apart.</summary>
    Hatch,

    /// <summary>A solid diamond about Start, its vertices Radius away, in Fill.</summary>
    FillDiamond,

    /// <summary>A diamond's outline about Start, its vertices Radius away.</summary>
    DiamondOutline,
}

/// <summary>What a stroke stands for, which picks its colour.</summary>
public enum TargetSchematicStrokeRole : byte
{
    /// <summary>The caster's station.</summary>
    Caster,

    /// <summary>The aimed tile's centre mark.</summary>
    Aim,

    /// <summary>How the activation gets from the caster to where it lands.</summary>
    Path,

    /// <summary>What it covers where it lands.</summary>
    Shape,

    /// <summary>What it would cover, drawn faint: it affects only the marked creature.</summary>
    FaintShape,

    /// <summary>A wall the path meets, and the ground a minimum range leaves out.</summary>
    Wall,

    /// <summary>One band of an aura's reach, in its own Fill: the aura's glow at its strength there.</summary>
    AuraBand,

    /// <summary>A drafting centre line through a centred shape.</summary>
    CentreLine,

    /// <summary>An extension line from what is measured to its dimension line.</summary>
    Extension,

    /// <summary>A dimension line, a leader, or their arrowheads.</summary>
    Dimension,
}

/// <summary>One stroke of a target schematic; which fields it reads is its Kind's.</summary>
public readonly record struct TargetSchematicStroke(
    TargetSchematicStrokeKind Kind,
    TargetSchematicStrokeRole Role,
    Vector2 Start,
    Vector2 End,
    float Radius,
    float StartRadians,
    float SweepRadians,
    float Thickness,
    StrokePattern Pattern,
    Color Fill = default)
{
    public static TargetSchematicStroke FillDiamond(TargetSchematicStrokeRole role, Vector2 centre, float halfDiagonal, Color fill) =>
        new(TargetSchematicStrokeKind.FillDiamond, role, centre, centre, halfDiagonal, 0f, 0f, 0f, default, fill);

    public static TargetSchematicStroke DiamondOutline(TargetSchematicStrokeRole role, Vector2 centre, float halfDiagonal, float thickness) =>
        new(TargetSchematicStrokeKind.DiamondOutline, role, centre, centre, halfDiagonal, 0f, 0f, thickness, default);

    public static TargetSchematicStroke Line(TargetSchematicStrokeRole role, Vector2 start, Vector2 end, float thickness, StrokePattern pattern = default) =>
        new(TargetSchematicStrokeKind.Line, role, start, end, 0f, 0f, 0f, thickness, pattern);

    public static TargetSchematicStroke Arc(TargetSchematicStrokeRole role, Vector2 centre, float radius, float startRadians, float sweepRadians, float thickness) =>
        new(TargetSchematicStrokeKind.Arc, role, centre, centre, radius, startRadians, sweepRadians, thickness, default);

    public static TargetSchematicStroke Circle(TargetSchematicStrokeRole role, Vector2 centre, float radius, float thickness) =>
        Arc(role, centre, radius, 0f, MathHelper.TwoPi, thickness);

    public static TargetSchematicStroke FillCircle(TargetSchematicStrokeRole role, Vector2 centre, float radius) =>
        new(TargetSchematicStrokeKind.FillCircle, role, centre, centre, radius, 0f, 0f, 0f, default);

    public static TargetSchematicStroke Arrowhead(TargetSchematicStrokeRole role, Vector2 tip, float pointingRadians, float length, float spreadRadians, float thickness) =>
        new(TargetSchematicStrokeKind.Arrowhead, role, tip, tip, length, pointingRadians, spreadRadians, thickness, default);

    public static TargetSchematicStroke CentreMark(TargetSchematicStrokeRole role, Vector2 centre, float halfSize, float thickness) =>
        new(TargetSchematicStrokeKind.CentreMark, role, centre, centre, halfSize, 0f, 0f, thickness, default);

    /// <param name="spacing">How far apart the two slashes are along the line; carried in End.X.</param>
    public static TargetSchematicStroke BreakMark(TargetSchematicStrokeRole role, Vector2 centre, float lineRadians, float halfLength, float spacing, float thickness) =>
        new(TargetSchematicStrokeKind.BreakMark, role, centre, new Vector2(spacing, 0f), halfLength, lineRadians, 0f, thickness, default);

    public static TargetSchematicStroke Hatch(TargetSchematicStrokeRole role, Vector2 topLeft, Vector2 size, float spacing, float thickness) =>
        new(TargetSchematicStrokeKind.Hatch, role, topLeft, size, spacing, 0f, 0f, thickness, default);

    /// <summary>How far apart a BreakMark's slashes are.</summary>
    public float BreakSpacing => End.X;

    /// <summary>A Hatch's size.</summary>
    public Vector2 HatchSize => End;

    /// <summary>This stroke moved by offset.</summary>
    public TargetSchematicStroke Translated(Vector2 offset) =>
        Kind is TargetSchematicStrokeKind.BreakMark or TargetSchematicStrokeKind.Hatch
            ? this with { Start = Start + offset }
            : this with { Start = Start + offset, End = End + offset };
}

/// <summary>Which value a label slot shows.</summary>
public enum TargetSchematicLabel : byte
{
    /// <summary>The farthest the aimed tile may be.</summary>
    Range,

    /// <summary>The shape's size: a radius, a length or a width.</summary>
    AreaSize,

    /// <summary>A Cone's width across its far end.</summary>
    ConeWidth,

    /// <summary>A Ring's inner radius: where its band begins.</summary>
    InnerRadius,

    /// <summary>The reach of the aura it places or holds, from its source to the diamond's edge.</summary>
    AuraRadius,

    /// <summary>Advanced: the nearest the aimed tile may be.</summary>
    MinimumRange,

    /// <summary>Advanced: how many creatures a projectile meets before it stops.</summary>
    HitCount,

    /// <summary>Advanced: a projectile flies on past the aim to its full range.</summary>
    PathEnd,

    /// <summary>Advanced: what walls do to the path, other than stop it.</summary>
    Walls,

    /// <summary>Advanced: the caster must see the aimed tile.</summary>
    AimSight,

    /// <summary>Advanced: walls hide parts of the shape from where it lands.</summary>
    ShapeSight,

    /// <summary>Advanced: in Target mode it affects only the marked creature.</summary>
    MarkedOnly,
}

/// <summary>An aura a schematic draws: its definition at the power and size it is placed or held with.</summary>
public sealed record TargetSchematicAura(AuraDefinition Aura, int Power, int Size);

/// <summary>What a shape's AreaSize dimension measures.</summary>
public enum TargetSchematicAreaMeasure : byte
{
    /// <summary>From the centre to the edge: a Circle, Ring, Square or Cross.</summary>
    Radius,

    /// <summary>Along the aim: a Line or Cone.</summary>
    Length,

    /// <summary>Across the aim: a Wall.</summary>
    Width,
}

/// <summary>Where a label sits against its slot's anchor.</summary>
public enum TargetSchematicLabelPlacement : byte
{
    /// <summary>Its bottom edge's centre on the anchor: above a dimension line.</summary>
    AboveCentred,

    /// <summary>Its bottom-left corner on the anchor: above a leader's shoulder.</summary>
    AboveStart,

    /// <summary>Its left edge's middle on the anchor: beside an upright dimension line.</summary>
    RightOf,
}

/// <summary>Where one value's label goes on a target schematic.</summary>
public readonly record struct TargetSchematicLabelSlot(TargetSchematicLabel Label, Vector2 Anchor, TargetSchematicLabelPlacement Placement)
{
    /// <summary>The top-left corner of a label of labelSize placed in this slot.</summary>
    public Vector2 TopLeftFor(Vector2 labelSize) => Placement switch
    {
        TargetSchematicLabelPlacement.AboveCentred => Anchor - new Vector2(labelSize.X / 2f, labelSize.Y),
        TargetSchematicLabelPlacement.AboveStart => Anchor - new Vector2(0f, labelSize.Y),
        _ => Anchor - new Vector2(0f, labelSize.Y / 2f),
    };

    /// <summary>This slot moved by offset.</summary>
    public TargetSchematicLabelSlot Translated(Vector2 offset) => this with { Anchor = Anchor + offset };
}
