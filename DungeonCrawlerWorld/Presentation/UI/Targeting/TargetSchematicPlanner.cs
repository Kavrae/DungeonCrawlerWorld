using Engine.Tiles;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;

namespace Presentation.UI.Targeting;

/// <summary>Plans a targeting spec's schematic: the caster's station, the path, the shape's glyph where it lands, and dimensions carrying the real distances.</summary>
/// <remarks>
/// Laid out left to right from the caster, every distance drawn at its band (TargetSchematicBands) rather than to scale; the
/// numbers are the labels'. Advanced mode adds a callout for each rule the spec holds that departs from the default
/// (CalloutsOf). Pure: it reads only the spec, the mode and the metrics.
/// </remarks>
public static class TargetSchematicPlanner
{
    private const TargetShape CentredShapes = TargetShape.Circle | TargetShape.Ring | TargetShape.Square | TargetShape.Cross;
    private const TargetShape RoundShapes = TargetShape.Circle | TargetShape.Ring;
    private const TargetShape LengthShapes = TargetShape.Line | TargetShape.Cone;

    private static readonly float ConeHalfAngleRadians = MathF.Atan(0.5f);

    /// <summary>Where along the drawn path a projectile's hit mark sits, as a fraction of it: before the middle, or after the break mark there when the path is broken.</summary>
    private static float HitMarkFraction(bool broken) => broken ? 0.68f : 0.3f;

    /// <summary>Where along the drawn path the aim sits when the path flies on past it.</summary>
    private static float AimBeforePathEndFraction(bool broken) => broken ? 0.8f : 0.62f;

    /// <summary>Where along the drawn path a wall block sits.</summary>
    private static float WallFraction(bool broken) => broken ? 0.9f : 0.8f;

    /// <summary>The largest schematic unit PlanToFit tries, in pixels.</summary>
    public const float LargestUnit = 16f;

    /// <summary>The smallest schematic unit PlanToFit shrinks to, in pixels; a schematic that still doesn't fit is drawn at it.</summary>
    public const float SmallestUnit = 7f;

    /// <summary>How much PlanToFit eases a path's stretch each time it doesn't fit.</summary>
    private const float PathStretchStep = 0.25f;

    /// <summary>Whether spec's schematic has a path, and so a Range dimension.</summary>
    public static bool ShowsRange(TargetingSpec spec) => spec.Anchor == TargetAnchor.PathEnd && spec.MaximumRange > 0;

    /// <summary>What spec's AreaSize dimension measures, or null when its shape has no size to show.</summary>
    public static TargetSchematicAreaMeasure? AreaMeasureOf(TargetingSpec spec) =>
        (spec.Shape & CentredShapes) != 0 ? TargetSchematicAreaMeasure.Radius
        : (spec.Shape & LengthShapes) != 0 ? TargetSchematicAreaMeasure.Length
        : spec.Shape.HasFlag(TargetShape.Wall) ? TargetSchematicAreaMeasure.Width
        : null;

    /// <summary>The callouts Advanced mode draws for spec, in the order they are listed: one per rule it holds that departs from the default.</summary>
    public static IReadOnlyList<TargetSchematicLabel> CalloutsOf(TargetingSpec spec)
    {
        var callouts = new List<TargetSchematicLabel>();
        var hasPath = ShowsRange(spec);
        if (hasPath && spec.MinimumRange > 0)
        {
            callouts.Add(TargetSchematicLabel.MinimumRange);
        }

        if (hasPath && spec.Path == TargetPath.Projectile)
        {
            callouts.Add(TargetSchematicLabel.HitCount);
            if (spec.PathEnd == ProjectilePathEnd.AtMaximumRange)
            {
                callouts.Add(TargetSchematicLabel.PathEnd);
            }
        }

        if (hasPath && spec.AimLineOfSight == LineOfSightRule.Required)
        {
            callouts.Add(TargetSchematicLabel.AimSight);
        }

        if (hasPath && spec.Path != TargetPath.Placed && spec.PathWalls != PathWallRule.StopsAtWall)
        {
            callouts.Add(TargetSchematicLabel.Walls);
        }

        if (spec.Shape != TargetShape.None && spec.ShapeLineOfSight == LineOfSightRule.Required)
        {
            callouts.Add(TargetSchematicLabel.ShapeSight);
        }

        if (spec.Shape != TargetShape.None && spec.TargetModeAffects == TargetModeAffects.MarkedOnly)
        {
            callouts.Add(TargetSchematicLabel.MarkedOnly);
        }

        return callouts;
    }

    /// <summary>spec's schematic fitting maximumSize: the largest unit, up to LargestUnit, that fits, its path stretched as far as it fits, up to TargetSchematicMetrics.MaximumPathStretch; at SmallestUnit and no stretch when nothing does.</summary>
    /// <remarks>Stretch goes before unit: a path is drawn at its band's length before anything is drawn smaller.</remarks>
    /// <param name="labelSizes">Each label's measured size; one it leaves out takes defaultLabelSize.</param>
    /// <param name="auras">The auras the activation places where it lands, drawn there under its shape; the first is measured.</param>
    public static TargetSchematic PlanToFit(TargetingSpec spec, DetailsMode mode, Vector2 defaultLabelSize, IReadOnlyDictionary<TargetSchematicLabel, Vector2>? labelSizes, Vector2 maximumSize, IReadOnlyList<TargetSchematicAura>? auras = null)
    {
        var unit = LargestUnit;
        var stretchesPath = ShowsRange(spec);
        while (true)
        {
            for (var stretch = stretchesPath ? TargetSchematicMetrics.MaximumPathStretch : 1f; stretch >= 1f; stretch -= PathStretchStep)
            {
                var schematic = Plan(spec, new TargetSchematicMetrics(unit, defaultLabelSize, stretch, labelSizes), mode, auras);
                if (schematic.Size.X <= maximumSize.X && schematic.Size.Y <= maximumSize.Y)
                {
                    return schematic;
                }
            }

            if (unit <= SmallestUnit)
            {
                return Plan(spec, new TargetSchematicMetrics(unit, defaultLabelSize, LabelSizes: labelSizes), mode, auras);
            }

            unit = MathF.Max(SmallestUnit, unit - 1f);
        }
    }

    /// <summary>The schematic of an aura held where its holder stands: the holder's station inside the aura's shaded diamond, and its radius.</summary>
    public static TargetSchematic PlanAuraReachToFit(TargetSchematicAura aura, Vector2 labelSize, Vector2 maximumSize)
    {
        var holder = new TargetingSpec(TargetShape.None, 0, Anchor: TargetAnchor.Caster);
        return PlanToFit(holder, DetailsMode.Simple, labelSize, null, maximumSize, [aura]);
    }

    /// <param name="auras">The auras the activation places where it lands, drawn there under its shape; the first is measured.</param>
    public static TargetSchematic Plan(TargetingSpec spec, TargetSchematicMetrics metrics, DetailsMode mode = DetailsMode.Simple, IReadOnlyList<TargetSchematicAura>? auras = null)
    {
        var callouts = mode == DetailsMode.Advanced ? CalloutsOf(spec) : [];
        var plan = new SchematicBuilder(metrics);

        var hasPath = ShowsRange(spec);
        var shapeLength = TargetSchematicBands.ShapeUnits(spec.AreaSize) * metrics.Unit;
        var auraStart = plan.Count;
        if (auras is not null)
        {
            foreach (var aura in auras.OrderByDescending(static aura => aura.Size))
            {
                AddAuraReach(plan, aura, metrics);
            }
        }

        var glyphStart = plan.Count;
        AddShapeGlyph(plan, spec.Shape, shapeLength, hasPath ? 0f : metrics.StationRadius + metrics.Gap, metrics);
        var glyphEnd = plan.Count;
        plan.Add(TargetSchematicStroke.FillCircle(TargetSchematicStrokeRole.Caster, Vector2.Zero, metrics.StationRadius));
        if (callouts.Contains(TargetSchematicLabel.MarkedOnly))
        {
            plan.RecastBetween(glyphStart, glyphEnd, static stroke => stroke with { Role = TargetSchematicStrokeRole.FaintShape });
        }

        if (callouts.Contains(TargetSchematicLabel.ShapeSight))
        {
            var pattern = metrics.ShapeSightPattern;
            plan.RecastBetween(glyphStart, glyphEnd, stroke => stroke.Kind is TargetSchematicStrokeKind.Line or TargetSchematicStrokeKind.Arc ? stroke with { Pattern = pattern } : stroke);
        }

        var anchor = Vector2.Zero;
        var aim = Vector2.Zero;
        var pathStart = Vector2.Zero;
        var pathEnd = Vector2.Zero;
        var wallBlockCentre = (Vector2?)null;
        if (hasPath)
        {
            var nearEdge = plan.BoundsBetween(auraStart, glyphEnd) is { } landing ? MathF.Max(0f, -landing.Left) : 0f;
            var pathStop = MathF.Max(nearEdge, metrics.CentreMarkHalfSize) + metrics.Gap;
            pathStart = new Vector2(metrics.StationRadius + metrics.Gap, 0f);
            var bandLength = TargetSchematicBands.PathUnits(spec.MaximumRange) * metrics.Unit * metrics.PathStretch;
            anchor = new Vector2(MathF.Max(bandLength, pathStart.X + metrics.MinimumPathLength + pathStop), 0f);
            pathEnd = new Vector2(anchor.X - pathStop, 0f);
            plan.TranslateBetween(auraStart, glyphEnd, anchor);

            var broken = TargetSchematicBands.IsPathBroken(spec.MaximumRange);
            aim = anchor;
            if (callouts.Contains(TargetSchematicLabel.PathEnd))
            {
                aim = Vector2.Lerp(pathStart, pathEnd, AimBeforePathEndFraction(broken));
                if (glyphStart == glyphEnd)
                {
                    plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Path, anchor - new Vector2(0f, metrics.EndTickHalfSize), anchor + new Vector2(0f, metrics.EndTickHalfSize), metrics.StrokeThickness));
                }
                else
                {
                    plan.Add(TargetSchematicStroke.CentreMark(TargetSchematicStrokeRole.Aim, anchor, metrics.CentreMarkHalfSize, metrics.ThinStrokeThickness));
                }
            }

            plan.Add(TargetSchematicStroke.CentreMark(TargetSchematicStrokeRole.Aim, aim, metrics.CentreMarkHalfSize, metrics.ThinStrokeThickness));
            AddPath(plan, spec, pathStart, pathEnd, metrics);

            if (callouts.Contains(TargetSchematicLabel.MinimumRange))
            {
                AddMinimumRangeZone(plan, spec, pathStart, pathEnd, metrics);
            }

            var hitLabelRightLimit = pathEnd.X - metrics.Gap;
            if (callouts.Contains(TargetSchematicLabel.Walls))
            {
                wallBlockCentre = Vector2.Lerp(pathStart, pathEnd, WallFraction(broken));
                AddWallBlock(plan, wallBlockCentre.Value, spec.PathWalls == PathWallRule.BouncesOffWalls, metrics);
                hitLabelRightLimit = wallBlockCentre.Value.X - metrics.WallBlockSize.X / 2f - metrics.WallFoldLength - metrics.Gap;
            }

            if (callouts.Contains(TargetSchematicLabel.HitCount))
            {
                AddHitMark(plan, Vector2.Lerp(pathStart, pathEnd, HitMarkFraction(broken)), hitLabelRightLimit, metrics);
            }
        }

        var glyphBounds = plan.BoundsBetween(glyphStart, glyphEnd);
        if ((spec.Shape & CentredShapes) != 0 && glyphBounds is { } centred)
        {
            AddCentreLines(plan, anchor, centred, metrics);
        }

        if (glyphBounds is { } wallBounds && AreaMeasureOf(spec) == TargetSchematicAreaMeasure.Width)
        {
            AddUprightDimension(plan, TargetSchematicLabel.AreaSize, wallBounds.Right + metrics.Gap, wallBounds.Right, wallBounds.Top, wallBounds.Bottom, metrics);
        }

        if (glyphBounds is { } coneBounds && spec.Shape.HasFlag(TargetShape.Cone))
        {
            var apexX = hasPath ? anchor.X : metrics.StationRadius + metrics.Gap;
            var coneEnd = Direction(ConeHalfAngleRadians) * shapeLength;
            AddUprightDimension(plan, TargetSchematicLabel.ConeWidth, apexX + coneEnd.X + metrics.Gap, coneBounds.Right, anchor.Y - coneEnd.Y, anchor.Y + coneEnd.Y, metrics);
        }

        var calloutColumnX = plan.AllBounds.Right + metrics.LeaderRun * 2f;
        if (glyphBounds is not null && AreaMeasureOf(spec) == TargetSchematicAreaMeasure.Radius)
        {
            AddRadiusLeader(plan, spec.Shape, anchor, shapeLength, metrics);
        }

        if (glyphBounds is not null && spec.Shape.HasFlag(TargetShape.Ring))
        {
            AddLeftLeader(plan, TargetSchematicLabel.InnerRadius, anchor + Direction(-MathHelper.PiOver4 * 3f) * RingInnerRadius(shapeLength, metrics), metrics);
        }

        AddCalloutColumn(plan, callouts, spec, anchor, aim, glyphBounds, wallBlockCentre, calloutColumnX, metrics);

        var landingBounds = plan.BoundsBetween(auraStart, glyphEnd);
        var rowY = plan.AllBounds.Bottom + metrics.Gap * 2f + metrics.SizeOf(TargetSchematicLabel.Range).Y;
        var rangeRowY = rowY;
        var rowUsed = false;
        var anchorExtensionTop = (landingBounds?.Bottom ?? metrics.EndTickHalfSize) + metrics.Gap;
        if (hasPath)
        {
            AddDimension(plan, TargetSchematicLabel.Range, 0f, anchor.X, rowY, metrics.StationRadius + metrics.Gap, anchorExtensionTop, metrics);
            rowUsed = true;
        }

        if (glyphBounds is { } lengthBounds && AreaMeasureOf(spec) == TargetSchematicAreaMeasure.Length)
        {
            var startX = hasPath ? anchor.X : 0f;
            var startExtensionTop = hasPath ? (float?)null : metrics.StationRadius + metrics.Gap;
            AddDimension(plan, TargetSchematicLabel.AreaSize, startX, lengthBounds.Right, rowY, startExtensionTop, lengthBounds.Bottom + metrics.Gap, metrics);
            rowUsed = true;
        }

        if (auras is { Count: > 0 })
        {
            var measured = auras[0];
            var auraY = rowUsed ? NextRow(rowY, TargetSchematicLabel.AuraRadius, metrics) : rowY;
            var startFrom = rowUsed ? rowY : metrics.StationRadius + metrics.Gap;
            var vertexX = anchor.X + AuraHalfDiagonal(measured, metrics);
            AddDimension(plan, TargetSchematicLabel.AuraRadius, anchor.X, vertexX, auraY, startFrom, anchor.Y + metrics.Gap, metrics);
            rowY = auraY;
            rowUsed = true;
        }

        if (callouts.Contains(TargetSchematicLabel.MinimumRange))
        {
            var minimumX = MinimumRangeX(spec, pathStart, pathEnd);
            var minimumY = NextRow(rowY, TargetSchematicLabel.MinimumRange, metrics);
            AddDimension(plan, TargetSchematicLabel.MinimumRange, 0f, minimumX, minimumY, rangeRowY, metrics.BarThickness / 2f + metrics.Gap, metrics);
        }

        return plan.Finish();
    }

    /// <summary>The dimension row under rowY, room left above it for label.</summary>
    private static float NextRow(float rowY, TargetSchematicLabel label, TargetSchematicMetrics metrics) =>
        rowY + metrics.SizeOf(label).Y + metrics.Gap * 2f;

    /// <summary>How far an aura's diamond reaches from its source, drawn at its size's band.</summary>
    private static float AuraHalfDiagonal(TargetSchematicAura aura, TargetSchematicMetrics metrics) =>
        TargetSchematicBands.ShapeUnits(aura.Size) * metrics.Unit;

    /// <summary>The most bands an aura's diamond is shaded in; a larger aura samples its distances evenly.</summary>
    private const int MaximumAuraBands = 6;

    /// <summary>How far toward the aura's glow a band is shaded at no strength and at full strength.</summary>
    private const float AuraWeakestShade = 0.15f;

    private const float AuraStrongestShade = 0.75f;

    /// <summary>An aura's reach about the origin: nested diamonds, outermost first, each in the aura's glow at its strength at that distance, then the outline.</summary>
    private static void AddAuraReach(SchematicBuilder plan, TargetSchematicAura aura, TargetSchematicMetrics metrics)
    {
        var halfDiagonal = AuraHalfDiagonal(aura, metrics);
        var bandCount = aura.Size <= 0 || aura.Aura.Falloff == AuraFalloff.None ? 1 : System.Math.Min(aura.Size + 1, MaximumAuraBands);
        for (var band = 0; band < bandCount; band++)
        {
            var distance = bandCount == 1 ? 0f : aura.Size - band * aura.Size / (float)(bandCount - 1);
            var bandHalfDiagonal = bandCount == 1 ? halfDiagonal : halfDiagonal * (distance + 0.5f) / (aura.Size + 0.5f);
            var strength = aura.Power <= 0 ? 0f : aura.Aura.Falloff.ValueAt(aura.Power, aura.Size, (int)MathF.Round(distance)) / (float)aura.Power;
            plan.Add(TargetSchematicStroke.FillDiamond(TargetSchematicStrokeRole.AuraBand, Vector2.Zero, bandHalfDiagonal, AuraBandColor(aura, strength)));
        }

        plan.Add(TargetSchematicStroke.DiamondOutline(TargetSchematicStrokeRole.Shape, Vector2.Zero, halfDiagonal, metrics.ThinStrokeThickness));
    }

    /// <summary>The panel's cell colour toward the aura's glow, the more the stronger the aura is there.</summary>
    public static Color AuraBandColor(TargetSchematicAura aura, float strength) =>
        Color.Lerp(UIEngine.UI.ColorPalettes.WindowPalette.PanelContentColor, aura.Aura.GlowColor, AuraWeakestShade + (AuraStrongestShade - AuraWeakestShade) * strength);

    private static void AddShapeGlyph(SchematicBuilder plan, TargetShape shape, float length, float leadIn, TargetSchematicMetrics metrics)
    {
        var thickness = metrics.StrokeThickness;
        if (shape.HasFlag(TargetShape.Circle))
        {
            plan.Add(TargetSchematicStroke.Circle(TargetSchematicStrokeRole.Shape, Vector2.Zero, length, thickness));
        }

        if (shape.HasFlag(TargetShape.Ring))
        {
            plan.Add(TargetSchematicStroke.Circle(TargetSchematicStrokeRole.Shape, Vector2.Zero, length, thickness));
            plan.Add(TargetSchematicStroke.Circle(TargetSchematicStrokeRole.Shape, Vector2.Zero, RingInnerRadius(length, metrics), thickness));
        }

        if (shape.HasFlag(TargetShape.Square))
        {
            plan.AddOutline(TargetSchematicStrokeRole.Shape, thickness, new(-length, -length), new(length, -length), new(length, length), new(-length, length));
        }

        if (shape.HasFlag(TargetShape.Cross))
        {
            var arm = length / 3f;
            plan.AddOutline(TargetSchematicStrokeRole.Shape, thickness,
                new(-arm, -length), new(arm, -length), new(arm, -arm), new(length, -arm), new(length, arm), new(arm, arm),
                new(arm, length), new(-arm, length), new(-arm, arm), new(-length, arm), new(-length, -arm), new(-arm, -arm));
        }

        if (shape.HasFlag(TargetShape.Line))
        {
            var half = metrics.BarThickness / 2f;
            plan.AddOutline(TargetSchematicStrokeRole.Shape, thickness, new(leadIn, -half), new(leadIn + length, -half), new(leadIn + length, half), new(leadIn, half));
        }

        if (shape.HasFlag(TargetShape.Cone))
        {
            var apex = new Vector2(leadIn, 0f);
            plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Shape, apex, apex + Direction(-ConeHalfAngleRadians) * length, thickness));
            plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Shape, apex, apex + Direction(ConeHalfAngleRadians) * length, thickness));
            plan.Add(TargetSchematicStroke.Arc(TargetSchematicStrokeRole.Shape, apex, length, -ConeHalfAngleRadians, ConeHalfAngleRadians * 2f, thickness));
        }

        if (shape.HasFlag(TargetShape.Wall))
        {
            var half = metrics.BarThickness / 2f;
            var centreX = leadIn > 0f ? leadIn + half : 0f;
            plan.AddOutline(TargetSchematicStrokeRole.Shape, thickness, new(centreX - half, -length), new(centreX + half, -length), new(centreX + half, length), new(centreX - half, length));
        }

        if (shape.HasFlag(TargetShape.Sweep))
        {
            var columnX = MathF.Max(leadIn + metrics.TileSize / 2f, metrics.TileSpacing);
            foreach (var row in (ReadOnlySpan<int>)[-1, 0, 1])
            {
                AddTile(plan, new Vector2(columnX, row * metrics.TileSpacing), metrics);
            }
        }

        if (shape.HasFlag(TargetShape.Adjacent))
        {
            for (var row = -1; row <= 1; row++)
            {
                for (var column = -1; column <= 1; column++)
                {
                    if (row != 0 || column != 0)
                    {
                        AddTile(plan, new Vector2(column, row) * metrics.TileSpacing, metrics);
                    }
                }
            }
        }

        if (shape.HasFlag(TargetShape.SingleTarget))
        {
            AddTile(plan, Vector2.Zero, metrics);
        }

        if (shape.HasFlag(TargetShape.Self))
        {
            plan.Add(TargetSchematicStroke.Circle(TargetSchematicStrokeRole.Shape, Vector2.Zero, metrics.StationRadius + metrics.Gap, thickness));
        }
    }

    /// <summary>A Ring glyph's inner circle, one band inside its outer circle of length.</summary>
    private static float RingInnerRadius(float length, TargetSchematicMetrics metrics) =>
        MathF.Max(metrics.StrokeThickness, length - metrics.RingBandThickness);

    private static void AddTile(SchematicBuilder plan, Vector2 centre, TargetSchematicMetrics metrics)
    {
        var half = metrics.TileSize / 2f;
        plan.AddOutline(TargetSchematicStrokeRole.Shape, metrics.ThinStrokeThickness,
            centre + new Vector2(-half, -half), centre + new Vector2(half, -half), centre + new Vector2(half, half), centre + new Vector2(-half, half));
    }

    private static void AddPath(SchematicBuilder plan, TargetingSpec spec, Vector2 start, Vector2 end, TargetSchematicMetrics metrics)
    {
        var thickness = metrics.StrokeThickness;
        var broken = TargetSchematicBands.IsPathBroken(spec.MaximumRange);
        if (spec.Path == TargetPath.Lobbed)
        {
            AddLob(plan, start, end, broken, metrics);
            return;
        }

        var pattern = spec.Path == TargetPath.Placed ? metrics.PlacedPathPattern : default;
        if (broken)
        {
            var middle = (start + end) / 2f;
            var halfGap = new Vector2(metrics.BreakGap / 2f, 0f);
            plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Path, start, middle - halfGap, thickness, pattern));
            plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Path, middle + halfGap, end, thickness, pattern));
            plan.Add(TargetSchematicStroke.BreakMark(TargetSchematicStrokeRole.Path, middle, 0f, metrics.BreakHalfLength, metrics.BreakGap, thickness));
        }
        else
        {
            plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Path, start, end, thickness, pattern));
        }

        if (spec.Path == TargetPath.Projectile)
        {
            plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Path, end, 0f, metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, thickness));
        }
    }

    /// <summary>An arc rising LobRise over the chord from start to end, ending in an arrowhead along it.</summary>
    private static void AddLob(SchematicBuilder plan, Vector2 start, Vector2 end, bool broken, TargetSchematicMetrics metrics)
    {
        var chord = end.X - start.X;
        var rise = MathF.Min(metrics.LobRise, chord / 2f);
        var radius = (chord * chord / 4f + rise * rise) / (2f * rise);
        var centre = new Vector2((start.X + end.X) / 2f, start.Y - rise + radius);
        var endRadians = MathF.Atan2(end.Y - centre.Y, end.X - centre.X);
        var startRadians = -MathHelper.Pi - endRadians;
        plan.Add(TargetSchematicStroke.Arc(TargetSchematicStrokeRole.Path, centre, radius, startRadians, endRadians - startRadians, metrics.StrokeThickness));
        plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Path, end, endRadians + MathHelper.PiOver2, metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, metrics.StrokeThickness));
        if (broken)
        {
            plan.Add(TargetSchematicStroke.BreakMark(TargetSchematicStrokeRole.Path, new Vector2(centre.X, start.Y - rise), 0f, metrics.BreakHalfLength, metrics.BreakGap, metrics.StrokeThickness));
        }
    }

    /// <summary>Where the minimum range falls on the drawn path: its share of the maximum range, kept clear of either end.</summary>
    private static float MinimumRangeX(TargetingSpec spec, Vector2 pathStart, Vector2 pathEnd)
    {
        var fraction = System.Math.Clamp(spec.MinimumRange / (float)System.Math.Max(1, spec.MaximumRange), 0.15f, 0.85f);
        return MathHelper.Lerp(pathStart.X, pathEnd.X, fraction);
    }

    /// <summary>The ground a minimum range leaves out: hatching along the path from the caster to the minimum, closed by a tick across the path.</summary>
    private static void AddMinimumRangeZone(SchematicBuilder plan, TargetingSpec spec, Vector2 pathStart, Vector2 pathEnd, TargetSchematicMetrics metrics)
    {
        var minimumX = MinimumRangeX(spec, pathStart, pathEnd);
        var half = metrics.BarThickness / 2f;
        plan.Add(TargetSchematicStroke.Hatch(TargetSchematicStrokeRole.Wall, new Vector2(pathStart.X, -half), new Vector2(minimumX - pathStart.X, half * 2f), metrics.HatchSpacing, metrics.ThinStrokeThickness));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Wall, new Vector2(minimumX, -half - metrics.Gap), new Vector2(minimumX, half + metrics.Gap), metrics.ThinStrokeThickness));
    }

    /// <summary>A circled X on the path where a projectile meets creatures, its label on a leader up and to the right when it ends before rightLimit (the shape, or a wall's fold), else up and to the left.</summary>
    private static void AddHitMark(SchematicBuilder plan, Vector2 centre, float rightLimit, TargetSchematicMetrics metrics)
    {
        var radius = metrics.HitMarkRadius;
        var cross = radius * 0.7f;
        plan.Add(TargetSchematicStroke.Circle(TargetSchematicStrokeRole.Path, centre, radius, metrics.ThinStrokeThickness));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Path, centre + new Vector2(-cross, -cross), centre + new Vector2(cross, cross), metrics.ThinStrokeThickness));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Path, centre + new Vector2(cross, -cross), centre + new Vector2(-cross, cross), metrics.ThinStrokeThickness));

        var top = centre - new Vector2(0f, radius);
        if (top.X + metrics.LeaderRun + metrics.SizeOf(TargetSchematicLabel.HitCount).X <= rightLimit)
        {
            AddLeader(plan, TargetSchematicLabel.HitCount, top, metrics);
            return;
        }

        AddLeftLeader(plan, TargetSchematicLabel.HitCount, top, metrics);
    }

    /// <summary>A hatched block of wall across the path; one the path bounces off folds it back from the block's near face.</summary>
    private static void AddWallBlock(SchematicBuilder plan, Vector2 centre, bool bounces, TargetSchematicMetrics metrics)
    {
        var size = metrics.WallBlockSize;
        var topLeft = centre - size / 2f;
        plan.Add(TargetSchematicStroke.Hatch(TargetSchematicStrokeRole.Wall, topLeft, size, metrics.HatchSpacing, metrics.ThinStrokeThickness));
        plan.AddOutline(TargetSchematicStrokeRole.Wall, metrics.ThinStrokeThickness, topLeft, topLeft + new Vector2(size.X, 0f), topLeft + size, topLeft + new Vector2(0f, size.Y));
        if (bounces)
        {
            var face = new Vector2(topLeft.X - metrics.Gap / 2f, centre.Y);
            var back = face + new Vector2(-metrics.WallFoldLength, -metrics.WallFoldLength);
            plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Path, face, back, metrics.StrokeThickness));
            plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Path, back, MathF.Atan2(back.Y - face.Y, back.X - face.X), metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, metrics.StrokeThickness));
        }
    }

    private static void AddCentreLines(SchematicBuilder plan, Vector2 centre, SchematicBounds glyph, TargetSchematicMetrics metrics)
    {
        var pattern = metrics.CentreLinePattern;
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.CentreLine, new(glyph.Left - metrics.Overshoot, centre.Y), new(glyph.Right + metrics.Overshoot, centre.Y), metrics.ThinStrokeThickness, pattern));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.CentreLine, new(centre.X, glyph.Top - metrics.Overshoot), new(centre.X, glyph.Bottom + metrics.Overshoot), metrics.ThinStrokeThickness, pattern));
    }

    /// <summary>A centred shape's size: a leader to its edge, up and to the right of its centre.</summary>
    private static void AddRadiusLeader(SchematicBuilder plan, TargetShape shape, Vector2 anchor, float length, TargetSchematicMetrics metrics)
    {
        var edgePoint = (shape & RoundShapes) != 0
            ? anchor + Direction(-MathHelper.PiOver4) * length
            : anchor + new Vector2(length, shape.HasFlag(TargetShape.Square) ? -length / 2f : -length / 3f);
        AddLeader(plan, TargetSchematicLabel.AreaSize, edgePoint, metrics);
    }

    /// <summary>The Advanced callouts with no place of their own, stacked in a column right of the drawing, each on a leader to what it is about.</summary>
    private static void AddCalloutColumn(SchematicBuilder plan, IReadOnlyList<TargetSchematicLabel> callouts, TargetingSpec spec, Vector2 anchor, Vector2 aim, SchematicBounds? glyph, Vector2? wallBlockCentre, float columnX, TargetSchematicMetrics metrics)
    {
        var rowY = anchor.Y;
        foreach (var callout in callouts)
        {
            Vector2? target = callout switch
            {
                TargetSchematicLabel.AimSight => aim,
                TargetSchematicLabel.PathEnd => anchor,
                TargetSchematicLabel.Walls when wallBlockCentre is { } wall => wall + new Vector2(metrics.WallBlockSize.X / 2f, -metrics.WallBlockSize.Y / 2f),
                TargetSchematicLabel.ShapeSight when glyph is { } shapeBounds => ShapeSightTarget(spec.Shape, anchor, shapeBounds),
                TargetSchematicLabel.MarkedOnly => anchor,
                _ => null,
            };
            if (target is not { } point)
            {
                continue;
            }

            var labelHeight = metrics.SizeOf(callout).Y;
            var labelLeft = new Vector2(columnX, rowY);
            var leaderEnd = labelLeft - new Vector2(metrics.Gap, 0f);
            var pointing = point - leaderEnd;
            plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Dimension, leaderEnd, point, metrics.ThinStrokeThickness));
            plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Dimension, point, MathF.Atan2(pointing.Y, pointing.X), metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, metrics.ThinStrokeThickness));
            plan.AddLabel(new TargetSchematicLabelSlot(callout, labelLeft, TargetSchematicLabelPlacement.RightOf));
            rowY += labelHeight + metrics.Gap;
        }
    }

    /// <summary>Where a leader about the shape's outline points: a round shape's edge down and to the right, else its glyph's lower right corner.</summary>
    private static Vector2 ShapeSightTarget(TargetShape shape, Vector2 anchor, SchematicBounds glyph)
    {
        if ((shape & RoundShapes) != 0)
        {
            return anchor + Direction(MathHelper.PiOver4) * ((glyph.Right - glyph.Left) / 2f);
        }

        return new Vector2(glyph.Right, glyph.Bottom);
    }

    /// <summary>A dimension from startX to endX on dimensionY: extension lines from each end's own y (none where it is null) to just past the line, the line, arrowheads outward and the label above.</summary>
    private static void AddDimension(SchematicBuilder plan, TargetSchematicLabel label, float startX, float endX, float dimensionY, float? startExtensionFrom, float endExtensionFrom, TargetSchematicMetrics metrics)
    {
        var thin = metrics.ThinStrokeThickness;
        if (startExtensionFrom is { } startFrom)
        {
            plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Extension, new(startX, startFrom), new(startX, PastLine(startFrom, dimensionY, metrics)), thin));
        }

        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Extension, new(endX, endExtensionFrom), new(endX, PastLine(endExtensionFrom, dimensionY, metrics)), thin));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Dimension, new(startX, dimensionY), new(endX, dimensionY), thin));
        plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Dimension, new(startX, dimensionY), MathHelper.Pi, metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, thin));
        plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Dimension, new(endX, dimensionY), 0f, metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, thin));
        plan.AddLabel(new TargetSchematicLabelSlot(label, new Vector2((startX + endX) / 2f, dimensionY - metrics.Gap), TargetSchematicLabelPlacement.AboveCentred));
    }

    /// <summary>Where an extension line from fromY stops: just past dimensionY, on the far side from where it started.</summary>
    private static float PastLine(float fromY, float dimensionY, TargetSchematicMetrics metrics) =>
        dimensionY + (dimensionY >= fromY ? metrics.Overshoot : -metrics.Overshoot);

    /// <summary>An upright dimension from top to bottom, right of objectRight: extension lines from extensionStart, the line, and the label right of it.</summary>
    private static void AddUprightDimension(SchematicBuilder plan, TargetSchematicLabel label, float extensionStart, float objectRight, float top, float bottom, TargetSchematicMetrics metrics)
    {
        var thin = metrics.ThinStrokeThickness;
        var lineX = objectRight + metrics.Gap + metrics.LeaderRun;
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Extension, new(extensionStart, top), new(lineX + metrics.Overshoot, top), thin));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Extension, new(extensionStart, bottom), new(lineX + metrics.Overshoot, bottom), thin));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Dimension, new(lineX, top), new(lineX, bottom), thin));
        plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Dimension, new(lineX, top), -MathHelper.PiOver2, metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, thin));
        plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Dimension, new(lineX, bottom), MathHelper.PiOver2, metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, thin));
        plan.AddLabel(new TargetSchematicLabelSlot(label, new Vector2(lineX + metrics.Overshoot + metrics.Gap, (top + bottom) / 2f), TargetSchematicLabelPlacement.RightOf));
    }

    /// <summary>A leader from a shoulder up and to the right of target, its arrowhead on target, the label above the shoulder.</summary>
    private static void AddLeader(SchematicBuilder plan, TargetSchematicLabel label, Vector2 target, TargetSchematicMetrics metrics)
    {
        var thin = metrics.ThinStrokeThickness;
        var elbow = target + new Vector2(metrics.LeaderRun, -metrics.LeaderRun);
        var pointing = target - elbow;
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Dimension, elbow, target, thin));
        plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Dimension, target, MathF.Atan2(pointing.Y, pointing.X), metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, thin));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Dimension, elbow, elbow + new Vector2(metrics.SizeOf(label).X, 0f), thin));
        plan.AddLabel(new TargetSchematicLabelSlot(label, elbow - new Vector2(0f, metrics.Gap / 2f), TargetSchematicLabelPlacement.AboveStart));
    }

    /// <summary>A leader from a shoulder up and to the left of target, its arrowhead on target, the label above the shoulder, ending at the elbow.</summary>
    private static void AddLeftLeader(SchematicBuilder plan, TargetSchematicLabel label, Vector2 target, TargetSchematicMetrics metrics)
    {
        var thin = metrics.ThinStrokeThickness;
        var elbow = target + new Vector2(-metrics.LeaderRun, -metrics.LeaderRun);
        var pointing = target - elbow;
        var labelWidth = metrics.SizeOf(label).X;
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Dimension, elbow, target, thin));
        plan.Add(TargetSchematicStroke.Arrowhead(TargetSchematicStrokeRole.Dimension, target, MathF.Atan2(pointing.Y, pointing.X), metrics.ArrowheadLength, metrics.ArrowheadSpreadRadians, thin));
        plan.Add(TargetSchematicStroke.Line(TargetSchematicStrokeRole.Dimension, elbow - new Vector2(labelWidth, 0f), elbow, thin));
        plan.AddLabel(new TargetSchematicLabelSlot(label, elbow - new Vector2(labelWidth, metrics.Gap / 2f), TargetSchematicLabelPlacement.AboveStart));
    }

    private static Vector2 Direction(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

    private readonly record struct SchematicBounds(float Left, float Top, float Right, float Bottom)
    {
        public SchematicBounds Union(SchematicBounds other) =>
            new(MathF.Min(Left, other.Left), MathF.Min(Top, other.Top), MathF.Max(Right, other.Right), MathF.Max(Bottom, other.Bottom));

        public static SchematicBounds Around(Vector2 centre, float halfWidth, float halfHeight) =>
            new(centre.X - halfWidth, centre.Y - halfHeight, centre.X + halfWidth, centre.Y + halfHeight);

        public static SchematicBounds Between(Vector2 first, Vector2 second, float margin) =>
            new(MathF.Min(first.X, second.X) - margin, MathF.Min(first.Y, second.Y) - margin, MathF.Max(first.X, second.X) + margin, MathF.Max(first.Y, second.Y) + margin);
    }

    private sealed class SchematicBuilder(TargetSchematicMetrics metrics)
    {
        private readonly List<TargetSchematicStroke> _strokes = [];
        private readonly List<TargetSchematicLabelSlot> _labels = [];

        public int Count => _strokes.Count;

        /// <summary>Every stroke and every label's reserved box so far.</summary>
        public SchematicBounds AllBounds
        {
            get
            {
                var bounds = BoundsBetween(0, _strokes.Count) ?? default;
                foreach (var slot in _labels)
                {
                    var size = metrics.SizeOf(slot.Label);
                    var topLeft = slot.TopLeftFor(size);
                    bounds = bounds.Union(new SchematicBounds(topLeft.X, topLeft.Y, topLeft.X + size.X, topLeft.Y + size.Y));
                }

                return bounds;
            }
        }

        public void Add(TargetSchematicStroke stroke) => _strokes.Add(stroke);

        public void AddLabel(TargetSchematicLabelSlot slot) => _labels.Add(slot);

        public void AddOutline(TargetSchematicStrokeRole role, float thickness, params ReadOnlySpan<Vector2> corners)
        {
            for (var index = 0; index < corners.Length; index++)
            {
                _strokes.Add(TargetSchematicStroke.Line(role, corners[index], corners[(index + 1) % corners.Length], thickness));
            }
        }

        public void TranslateBetween(int firstIndex, int endIndex, Vector2 offset) =>
            RecastBetween(firstIndex, endIndex, stroke => stroke.Translated(offset));

        public void RecastBetween(int firstIndex, int endIndex, Func<TargetSchematicStroke, TargetSchematicStroke> recast)
        {
            for (var index = firstIndex; index < endIndex; index++)
            {
                _strokes[index] = recast(_strokes[index]);
            }
        }

        /// <summary>The bounds of the strokes from firstIndex up to, not including, endIndex; null when there are none.</summary>
        public SchematicBounds? BoundsBetween(int firstIndex, int endIndex)
        {
            SchematicBounds? bounds = null;
            for (var index = firstIndex; index < endIndex; index++)
            {
                var strokeBounds = BoundsOf(_strokes[index]);
                bounds = bounds is { } current ? current.Union(strokeBounds) : strokeBounds;
            }

            return bounds;
        }

        public TargetSchematic Finish()
        {
            var bounds = AllBounds;
            var offset = new Vector2(-bounds.Left, -bounds.Top);
            var strokes = new TargetSchematicStroke[_strokes.Count];
            for (var index = 0; index < strokes.Length; index++)
            {
                strokes[index] = _strokes[index].Translated(offset);
            }

            var labels = new TargetSchematicLabelSlot[_labels.Count];
            for (var index = 0; index < labels.Length; index++)
            {
                labels[index] = _labels[index].Translated(offset);
            }

            return new TargetSchematic(new Vector2(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top), offset, strokes, labels);
        }

        private static SchematicBounds BoundsOf(TargetSchematicStroke stroke)
        {
            var margin = stroke.Thickness / 2f;
            switch (stroke.Kind)
            {
                case TargetSchematicStrokeKind.Line:
                    return SchematicBounds.Between(stroke.Start, stroke.End, margin);
                case TargetSchematicStrokeKind.Hatch:
                    return SchematicBounds.Between(stroke.Start, stroke.Start + stroke.HatchSize, margin);
                case TargetSchematicStrokeKind.Arc:
                {
                    var segmentCount = UIEngine.Rendering.StrokeRenderer.ArcSegmentCount(stroke.Radius, stroke.SweepRadians);
                    var bounds = SchematicBounds.Around(stroke.Start + Direction(stroke.StartRadians) * stroke.Radius, margin, margin);
                    for (var segment = 1; segment <= segmentCount; segment++)
                    {
                        var point = stroke.Start + Direction(stroke.StartRadians + stroke.SweepRadians * segment / segmentCount) * stroke.Radius;
                        bounds = bounds.Union(SchematicBounds.Around(point, margin, margin));
                    }

                    return bounds;
                }

                case TargetSchematicStrokeKind.Arrowhead:
                {
                    var backwards = stroke.StartRadians + MathHelper.Pi;
                    return SchematicBounds.Between(stroke.Start + Direction(backwards - stroke.SweepRadians) * stroke.Radius, stroke.Start + Direction(backwards + stroke.SweepRadians) * stroke.Radius, margin)
                        .Union(SchematicBounds.Around(stroke.Start, margin, margin));
                }

                case TargetSchematicStrokeKind.BreakMark:
                {
                    var reach = stroke.Radius + stroke.BreakSpacing;
                    return SchematicBounds.Around(stroke.Start, reach, reach);
                }

                default:
                    return SchematicBounds.Around(stroke.Start, stroke.Radius + margin, stroke.Radius + margin);
            }
        }
    }
}
