namespace Engine.Math;

/// <summary>
/// Shared falloff math for anything that radiates outward from a source and halves per tile
/// of distance -- used by status-effect auras and tile tinting alike. Kept here rather than
/// duplicated in each because both need the exact same "strength halves per tile, floors at
/// 0" rule; ValueAtDistance/MaxRadius are metric-agnostic (just take/produce a plain integer
/// distance), so callers pick whichever distance function fits their shape. Pure grid math
/// with no game-specific knowledge -- lives alongside Vector3Int/MathUtility rather than in a
/// Game-layer namespace, since both Game and Presentation callers need it (Presentation
/// depending on a Game-layer algorithm, rather than just reading a component's data, would be
/// a real layering leak; depending downward on Engine from both sides is exactly what the
/// one-way layering rule wants).
///
/// Both ChebyshevDistance (square/chessboard, diagonals cost 1 tile the same as
/// orthogonal) and ManhattanDistance (diamond, diagonals cost 2) are kept -- status-effect
/// auras currently use Manhattan, but Chebyshev remains here for whatever future feature
/// wants a square falloff shape instead of a diamond one.
/// </summary>
public static class DistanceFalloff
{
    public static int ChebyshevDistance(Vector3Int a, Vector3Int b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    public static int ManhattanDistance(Vector3Int a, Vector3Int b) =>
        System.Math.Abs(a.X - b.X) + System.Math.Abs(a.Y - b.Y);

    /// <summary>strength halved per tile of distance, floored at 0. E.g. strength 8: 8, 4, 2, 1, 0, 0, ... at distances 0, 1, 2, 3, 4, 5.</summary>
    public static int ValueAtDistance(int strength, int distance) =>
        distance < 0 ? 0 : strength >> distance;

    /// <summary>Furthest tile at which ValueAtDistance(strength, ...) is still > 0, or -1 if strength itself is <= 0.</summary>
    public static int MaxRadius(int strength) => strength <= 0 ? -1 : (int)System.Math.Log2(strength);

    public delegate void ManhattanCellVisitor(Vector3Int cellPosition, int contribution);
    public delegate void ManhattanCellVisitor<in TState>(Vector3Int cellPosition, int contribution, TState state);

    /// <summary>
    /// Visits every cell within a Manhattan-distance falloff radius of sourcePosition (same Z
    /// layer), clamped to a mapSize.X x mapSize.Y grid, calling visit(cellPosition,
    /// contribution) for each cell whose ValueAtDistance(strength, distance) is > 0 -- shared
    /// by AuraGrid.Splat and MapWindow.BuildTintGrid, which scatter the identical falloff
    /// shape onto two different per-cell accumulators (a signed integer total vs. a
    /// weighted-color sum). The diamond loop bounds (deltaX limited to
    /// maxRadius - |deltaY| per row) visit only cells actually within range, rather than a
    /// full square scan that then discards out-of-range corners.
    ///
    /// Thin wrapper over the TState overload below -- existing callers here already pass a
    /// capturing lambda (both current ones run once at construction time, not per-frame, so the
    /// closure allocation was never a real cost for them), so this keeps their call sites
    /// unchanged rather than forcing every caller to adopt the state-passing shape. Callers that
    /// run every frame (e.g. TargetShapeResolver, for live hover tracking) should use the TState
    /// overload with a static lambda instead, to avoid allocating a new closure on every call.
    /// </summary>
    public static void ScatterManhattan(Vector3Int sourcePosition, int strength, Vector3Int mapSize, ManhattanCellVisitor visit) =>
        ScatterManhattan(sourcePosition, strength, mapSize, visit, static (cellPosition, contribution, state) => state(cellPosition, contribution));

    /// <summary>See the non-generic overload above for the shared shape/bounds rationale. state is threaded through to visit unchanged, so a caller can pass a static lambda plus whatever state it needs (e.g. a results buffer) without allocating a closure per call.</summary>
    public static void ScatterManhattan<TState>(Vector3Int sourcePosition, int strength, Vector3Int mapSize, TState state, ManhattanCellVisitor<TState> visit)
    {
        var maxRadius = MaxRadius(strength);
        if (maxRadius < 0)
        {
            return;
        }

        for (var deltaY = -maxRadius; deltaY <= maxRadius; deltaY++)
        {
            var cellY = sourcePosition.Y + deltaY;
            if (cellY < 0 || cellY >= mapSize.Y)
            {
                continue;
            }

            var remainingRadius = maxRadius - System.Math.Abs(deltaY);
            for (var deltaX = -remainingRadius; deltaX <= remainingRadius; deltaX++)
            {
                var cellX = sourcePosition.X + deltaX;
                if (cellX < 0 || cellX >= mapSize.X)
                {
                    continue;
                }

                var cellPosition = new Vector3Int(cellX, cellY, sourcePosition.Z);
                var contribution = ValueAtDistance(strength, ManhattanDistance(sourcePosition, cellPosition));
                if (contribution <= 0)
                {
                    continue;
                }

                visit(cellPosition, contribution, state);
            }
        }
    }
}
