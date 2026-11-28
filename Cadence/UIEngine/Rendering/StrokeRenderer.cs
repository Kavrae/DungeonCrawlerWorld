using Microsoft.Xna.Framework;

namespace UIEngine.Rendering;

/// <summary>Draws lines, outlines and drafting marks out of filled strips: what a schematic or a dimension drawing is made of.</summary>
/// <remarks>
/// Every stroke is one or more thickness-tall rectangles turned to match (ISpriteDrawTarget.FillRectangle), centred across
/// the path it follows; the renderer has no line primitive. Strokes of a translucent colour overlap where they meet.
/// </remarks>
public static class StrokeRenderer
{
    /// <summary>Lengths an arc's segments are kept under, in pixels, so a large circle stays round.</summary>
    public const float MaximumArcSegmentLength = 4f;

    /// <summary>The fewest segments any arc is drawn with, so a tiny circle is still more than a polygon.</summary>
    public const int MinimumArcSegmentCount = 8;

    /// <summary>How many rotated slivers a filled circle is swept from.</summary>
    public const int CircleSliverCount = 48;

    private static readonly Vector2 CentredAcross = new(0f, 0.5f);
    private static readonly Vector2 CentredBoth = new(0.5f, 0.5f);

    /// <summary>A straight line from start to end, thickness wide, centred on the line between them.</summary>
    public static void Line<TTarget>(TTarget target, Vector2 start, Vector2 end, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= 0f || thickness <= 0f)
        {
            return;
        }

        target.FillRectangle(start, new Vector2(length, thickness), MathF.Atan2(delta.Y, delta.X), CentredAcross, color, style);
    }

    /// <summary>A line from start to end broken by pattern, starting with a dash; a dash the end cuts short is drawn short.</summary>
    public static void PatternedLine<TTarget>(TTarget target, Vector2 start, Vector2 end, float thickness, StrokePattern pattern, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        var delta = end - start;
        var length = delta.Length();
        if (length <= 0f || pattern.Dash <= 0f)
        {
            return;
        }

        var direction = delta / length;
        var distance = 0f;
        while (distance < length)
        {
            distance = DrawPatternPiece(target, start, direction, length, distance, pattern.Dash, thickness, color, style) + pattern.Gap;
            if (pattern.Dot > 0f && distance < length)
            {
                distance = DrawPatternPiece(target, start, direction, length, distance, pattern.Dot, thickness, color, style) + pattern.Gap;
            }
        }
    }

    private static float DrawPatternPiece<TTarget>(TTarget target, Vector2 start, Vector2 direction, float length, float distance, float pieceLength, float thickness, Color color, SpriteDrawStyle style)
        where TTarget : ISpriteDrawTarget
    {
        var pieceEnd = MathF.Min(distance + pieceLength, length);
        Line(target, start + direction * distance, start + direction * pieceEnd, thickness, color, style);
        return pieceEnd;
    }

    /// <summary>How many segments an arc of radius sweeping sweepRadians is drawn with.</summary>
    public static int ArcSegmentCount(float radius, float sweepRadians) =>
        System.Math.Max(MinimumArcSegmentCount, (int)MathF.Ceiling(MathF.Abs(sweepRadians) * radius / MaximumArcSegmentLength));

    /// <summary>Part of a circle's outline, from startRadians sweeping sweepRadians (positive is clockwise on screen, y down).</summary>
    public static void ArcOutline<TTarget>(TTarget target, Vector2 centre, float radius, float startRadians, float sweepRadians, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        if (radius <= 0f)
        {
            return;
        }

        var segmentCount = ArcSegmentCount(radius, sweepRadians);
        var step = sweepRadians / segmentCount;
        var previous = PointOnCircle(centre, radius, startRadians);
        for (var segment = 1; segment <= segmentCount; segment++)
        {
            var next = PointOnCircle(centre, radius, startRadians + step * segment);
            Line(target, previous, next, thickness, color, style);
            previous = next;
        }
    }

    /// <summary>Part of a circle's outline broken by pattern along its length, starting with a dash: each segment is drawn when its middle falls in a dash or dot.</summary>
    public static void PatternedArcOutline<TTarget>(TTarget target, Vector2 centre, float radius, float startRadians, float sweepRadians, float thickness, StrokePattern pattern, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        if (radius <= 0f || pattern.Dash <= 0f)
        {
            return;
        }

        var segmentCount = ArcSegmentCount(radius, sweepRadians);
        var step = sweepRadians / segmentCount;
        var segmentLength = MathF.Abs(step) * radius;
        var previous = PointOnCircle(centre, radius, startRadians);
        for (var segment = 1; segment <= segmentCount; segment++)
        {
            var next = PointOnCircle(centre, radius, startRadians + step * segment);
            if (pattern.IsOnAt((segment - 0.5f) * segmentLength))
            {
                Line(target, previous, next, thickness, color, style);
            }

            previous = next;
        }
    }

    /// <summary>A whole circle's outline.</summary>
    public static void CircleOutline<TTarget>(TTarget target, Vector2 centre, float radius, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget =>
        ArcOutline(target, centre, radius, 0f, MathHelper.TwoPi, thickness, color, style);

    /// <summary>A solid disc, swept from thin rotated slivers about its centre.</summary>
    public static void FillCircle<TTarget>(TTarget target, Vector2 centre, float radius, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        if (radius <= 0f)
        {
            return;
        }

        var sliverSize = new Vector2(radius, MathF.Max(1f, MathHelper.TwoPi * radius / CircleSliverCount * 1.5f));
        var angleStep = MathHelper.TwoPi / CircleSliverCount;
        for (var sliver = 0; sliver < CircleSliverCount; sliver++)
        {
            target.FillRectangle(centre, sliverSize, -angleStep * sliver, CentredAcross, color, style);
        }
    }

    /// <summary>A solid diamond: a square turned 45 degrees, its vertices halfDiagonal from centre straight up, down, left and right.</summary>
    public static void FillDiamond<TTarget>(TTarget target, Vector2 centre, float halfDiagonal, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        if (halfDiagonal <= 0f)
        {
            return;
        }

        var side = halfDiagonal * MathF.Sqrt(2f);
        target.FillRectangle(centre, new Vector2(side, side), MathHelper.PiOver4, CentredBoth, color, style);
    }

    /// <summary>A diamond's outline, its vertices halfDiagonal from centre straight up, down, left and right.</summary>
    public static void DiamondOutline<TTarget>(TTarget target, Vector2 centre, float halfDiagonal, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        var top = centre + new Vector2(0f, -halfDiagonal);
        var right = centre + new Vector2(halfDiagonal, 0f);
        var bottom = centre + new Vector2(0f, halfDiagonal);
        var left = centre + new Vector2(-halfDiagonal, 0f);
        Line(target, top, right, thickness, color, style);
        Line(target, right, bottom, thickness, color, style);
        Line(target, bottom, left, thickness, color, style);
        Line(target, left, top, thickness, color, style);
    }

    /// <summary>An open arrowhead at tip, pointing along pointingRadians: two strokes length long, each spreadRadians off the line behind the tip.</summary>
    public static void OpenArrowhead<TTarget>(TTarget target, Vector2 tip, float pointingRadians, float length, float spreadRadians, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        var backwards = pointingRadians + MathHelper.Pi;
        Line(target, tip, PointOnCircle(tip, length, backwards - spreadRadians), thickness, color, style);
        Line(target, tip, PointOnCircle(tip, length, backwards + spreadRadians), thickness, color, style);
    }

    /// <summary>A drafting centre mark: a small plus, each arm halfSize from centre.</summary>
    public static void CentreMark<TTarget>(TTarget target, Vector2 centre, float halfSize, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        Line(target, centre - new Vector2(halfSize, 0f), centre + new Vector2(halfSize, 0f), thickness, color, style);
        Line(target, centre - new Vector2(0f, halfSize), centre + new Vector2(0f, halfSize), thickness, color, style);
    }

    /// <summary>A break mark: two parallel slashes across a line running along lineRadians, centred on centre, spacing apart.</summary>
    /// <remarks>Each slash leans 30 degrees off square to the line, as a drafted break does.</remarks>
    public static void BreakMark<TTarget>(TTarget target, Vector2 centre, float lineRadians, float halfLength, float spacing, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        var along = new Vector2(MathF.Cos(lineRadians), MathF.Sin(lineRadians));
        var slashRadians = lineRadians - MathHelper.PiOver2 + MathHelper.Pi / 6f;
        var slash = new Vector2(MathF.Cos(slashRadians), MathF.Sin(slashRadians)) * halfLength;
        foreach (var side in (ReadOnlySpan<float>)[-0.5f, 0.5f])
        {
            var slashCentre = centre + along * (spacing * side);
            Line(target, slashCentre - slash, slashCentre + slash, thickness, color, style);
        }
    }

    /// <summary>Fills the area at topLeft of size with diagonal hatching, spacing apart along each edge, each line clipped to the area.</summary>
    public static void HatchRectangle<TTarget>(TTarget target, Vector2 topLeft, Vector2 size, float spacing, float thickness, Color color, SpriteDrawStyle style = default)
        where TTarget : ISpriteDrawTarget
    {
        if (spacing <= 0f || size.X <= 0f || size.Y <= 0f)
        {
            return;
        }

        var left = topLeft.X;
        var top = topLeft.Y;
        var right = left + size.X;
        var bottom = top + size.Y;
        for (var offset = spacing / 2f; offset < size.X + size.Y; offset += spacing)
        {
            var start = offset <= size.X
                ? new Vector2(left + offset, bottom)
                : new Vector2(right, bottom - (offset - size.X));
            var end = offset >= size.Y
                ? new Vector2(left + offset - size.Y, top)
                : new Vector2(left, bottom - offset);
            Line(target, start, end, thickness, color, style);
        }
    }

    private static Vector2 PointOnCircle(Vector2 centre, float radius, float radians) =>
        centre + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * radius;
}

/// <summary>How a patterned line breaks up: Dash, Gap, and, when Dot is set, Dot and Gap again -- a drafting centre line.</summary>
public readonly record struct StrokePattern(float Dash, float Gap, float Dot = 0f)
{
    /// <summary>Evenly dashed.</summary>
    public static StrokePattern Dashed(float dash, float gap) => new(dash, gap);

    /// <summary>A drafting centre line: a long dash, a gap, a dot, a gap.</summary>
    public static StrokePattern CentreLine(float dash, float gap, float dot) => new(dash, gap, dot);

    /// <summary>Whether the point distance along a line drawn in this pattern falls in a dash or dot rather than a gap.</summary>
    public bool IsOnAt(float distance)
    {
        var period = Dot > 0f ? Dash + Gap + Dot + Gap : Dash + Gap;
        if (period <= 0f)
        {
            return true;
        }

        var position = distance % period;
        return position < Dash || (Dot > 0f && position >= Dash + Gap && position < Dash + Gap + Dot);
    }
}
