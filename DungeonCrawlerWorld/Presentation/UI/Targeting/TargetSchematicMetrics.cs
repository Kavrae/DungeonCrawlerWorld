using Microsoft.Xna.Framework;
using UIEngine.Rendering;

namespace Presentation.UI.Targeting;

/// <summary>How big a target schematic is drawn: one schematic unit in pixels, and the box reserved for each label; every other size follows from them.</summary>
/// <param name="Unit">One schematic unit, about a glyph's width: the bands' lengths are counted in it.</param>
/// <param name="LabelSize">The box a label slot reserves when LabelSizes names no size of its own.</param>
/// <param name="PathStretch">How many times its band's length a path is drawn: 1 at its tightest, up to MaximumPathStretch where there is room.</param>
/// <param name="LabelSizes">Each label's own box, its text's measured size; a label it leaves out takes LabelSize.</param>
public sealed record TargetSchematicMetrics(float Unit, Vector2 LabelSize, float PathStretch = 1f, IReadOnlyDictionary<TargetSchematicLabel, Vector2>? LabelSizes = null)
{
    /// <summary>The most a path is stretched past its band's length.</summary>
    public const float MaximumPathStretch = 3f;

    /// <summary>The box label's slot reserves.</summary>
    public Vector2 SizeOf(TargetSchematicLabel label) =>
        LabelSizes is not null && LabelSizes.TryGetValue(label, out var size) ? size : LabelSize;

    public float StationRadius => Unit * 0.28f;

    public float StrokeThickness => MathF.Max(1f, MathF.Round(Unit / 8f));

    public float ThinStrokeThickness => 1f;

    public float Gap => MathF.Max(2f, Unit * 0.15f);

    public float Overshoot => Unit * 0.25f;

    public float ArrowheadLength => Unit * 0.3f;

    public float ArrowheadSpreadRadians => MathHelper.Pi / 7f;

    public float CentreMarkHalfSize => Unit * 0.2f;

    public float BreakGap => Unit * 0.35f;

    public float BreakHalfLength => Unit * 0.25f;

    public float MinimumPathLength => Unit * 0.75f;

    public float LobRise => Unit * 0.75f;

    public float BarThickness => Unit * 0.3f;

    public float RingBandThickness => Unit * 0.3f;

    public float TileSize => Unit * 0.45f;

    public float TileSpacing => Unit * 0.55f;

    public float LeaderRun => Unit * 0.55f;

    public float HitMarkRadius => Unit * 0.25f;

    public float EndTickHalfSize => Unit * 0.3f;

    public Vector2 WallBlockSize => new(Unit * 0.35f, Unit * 2.2f);

    public float WallFoldLength => Unit * 0.8f;

    public float HatchSpacing => MathF.Max(3f, Unit * 0.25f);

    public StrokePattern PlacedPathPattern => StrokePattern.Dashed(Unit * 0.12f, Unit * 0.2f);

    public StrokePattern CentreLinePattern => StrokePattern.CentreLine(Unit * 0.6f, Unit * 0.12f, Unit * 0.1f);

    public StrokePattern ShapeSightPattern => StrokePattern.Dashed(Unit * 0.4f, Unit * 0.25f);
}
