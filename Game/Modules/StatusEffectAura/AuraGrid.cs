using Engine.Math;
using Game.Modules.StatusEffects;

namespace Game.Modules.StatusEffectAura;

/// <summary>
/// Precomputed per-cell, per-effect-type total stack potential across the whole map -- a
/// single shared sparse index for every StatusEffectType at once.
///
/// Built once, lazily (see StatusEffectAuraSystem.EnsureGrid), by scattering every
/// currently-registered source's falloff into it, then kept in sync incrementally as sources
/// move (AddSource/RemoveSource).
///
/// GetTotalStacksAt is an O(1) dictionary lookup.
///
/// Uses Manhattan distance (diamond-shaped falloff).
/// </summary>
public sealed class AuraGrid
{
    private readonly Dictionary<(int CellIndex, StatusEffectType EffectType), int> _totalStacksByCellAndEffectType = [];
    private readonly Vector3Int _mapSize;

    public AuraGrid(Vector3Int mapSize)
    {
        _mapSize = mapSize;
    }

    public int GetTotalStacksAt(Vector3Int position, StatusEffectType effectType) =>
        IsOnMap(position) ? _totalStacksByCellAndEffectType.GetValueOrDefault((position.FlatIndex(_mapSize), effectType)) : 0;

    public void AddSource(Vector3Int sourcePosition, int strength, StatusEffectType effectType) => Splat(sourcePosition, strength, effectType, sign: 1);

    public void RemoveSource(Vector3Int sourcePosition, int strength, StatusEffectType effectType) => Splat(sourcePosition, strength, effectType, sign: -1);

    private void Splat(Vector3Int sourcePosition, int strength, StatusEffectType effectType, int sign)
    {
        DistanceFalloff.ScatterManhattan(sourcePosition, strength, _mapSize, (cellPosition, contribution) =>
        {
            var key = (cellPosition.FlatIndex(_mapSize), effectType);
            var newTotal = _totalStacksByCellAndEffectType.GetValueOrDefault(key) + sign * contribution;

            // Remove rather than store a zero -- keeps the dictionary's size proportional to
            // cells actually under some source's influence right now, not to every cell any
            // source has ever touched.
            if (newTotal == 0)
            {
                _totalStacksByCellAndEffectType.Remove(key);
            }
            else
            {
                _totalStacksByCellAndEffectType[key] = newTotal;
            }
        });
    }

    private bool IsOnMap(Vector3Int position) =>
        position.X >= 0 && position.Y >= 0 && position.Z >= 0
        && position.X < _mapSize.X && position.Y < _mapSize.Y && position.Z < _mapSize.Z;
}
