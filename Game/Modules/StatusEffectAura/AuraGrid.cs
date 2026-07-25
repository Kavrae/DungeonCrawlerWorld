using Engine.Math;

namespace Game.Modules.StatusEffectAura;

/// <summary>
/// Precomputed per-cell total stack potential, across the whole map, for a single status
/// effect type. Built once, lazily (see StatusEffectAuraSystem.EnsureGrids -- one instance of
/// this class per distinct StatusEffectType actually in use, since summing contributions from
/// sources of *different* effect types into one shared total would be meaningless), by
/// scattering every currently-registered same-effect-type source's falloff onto it, then kept
/// in sync incrementally as sources move (AddSource/RemoveSource) -- not recomputed live for
/// every entity that moves.
///
/// GetTotalStacksAt is an O(1) array read. This replaces an earlier version of this feature
/// that recomputed a full falloff-radius box scan for every single EntityMoved in the game --
/// correct, but a measured production performance bug (see this class's git history / the
/// earlier BurningAuraGrid it was renamed from): every creature's every move paid the cost,
/// not just movers near a source.
///
/// Uses Manhattan distance (diamond-shaped falloff), not Chebyshev (square) -- see
/// DistanceFalloff.ScatterManhattan, which this and MapWindow's tint grid both scatter
/// through, so the falloff shape is defined in exactly one place.
/// </summary>
public sealed class AuraGrid
{
    private readonly int[] _totalStacksByPosition;
    private readonly Vector3Int _mapSize;

    public AuraGrid(Vector3Int mapSize)
    {
        _mapSize = mapSize;
        _totalStacksByPosition = new int[mapSize.Volume];
    }

    public int GetTotalStacksAt(Vector3Int position) =>
        IsOnMap(position) ? _totalStacksByPosition[position.FlatIndex(_mapSize)] : 0;

    public void AddSource(Vector3Int sourcePosition, int strength) => Splat(sourcePosition, strength, sign: 1);

    public void RemoveSource(Vector3Int sourcePosition, int strength) => Splat(sourcePosition, strength, sign: -1);

    private void Splat(Vector3Int sourcePosition, int strength, int sign)
    {
        DistanceFalloff.ScatterManhattan(sourcePosition, strength, _mapSize, (cellPosition, contribution) =>
        {
            _totalStacksByPosition[cellPosition.FlatIndex(_mapSize)] += sign * contribution;
        });
    }

    private bool IsOnMap(Vector3Int position) =>
        position.X >= 0 && position.Y >= 0 && position.Z >= 0
        && position.X < _mapSize.X && position.Y < _mapSize.Y && position.Z < _mapSize.Z;
}
