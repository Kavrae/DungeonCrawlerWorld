using Engine.Math;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules.StatusEffectAura;

/// <summary>
/// Precomputed per-cell, per-effect-type total stack potential across the whole map -- a
/// sparse index for every StatusEffectType at once.
///
/// Built once, lazily (see StatusEffectAuraSystem.EnsureGrid), by scattering every
/// currently-registered source's falloff into it, then kept in sync incrementally as sources
/// move (AddSource/RemoveSource).
///
/// One NeighborhoodCells per effect type, indexed by the type's value, so a lookup is an array
/// read plus a per-neighborhood dictionary lookup, and a neighborhood's totals can be dropped
/// with it.
///
/// Uses Manhattan distance (diamond-shaped falloff).
/// </summary>
public sealed class AuraGrid
{
    private readonly NeighborhoodCells<int>?[] _totalStacksByEffectType = new NeighborhoodCells<int>?[byte.MaxValue + 1];
    private readonly IMapQuery _map;

    /// <param name="map">Read for its bounds at every scatter, so contributions past the map's edge are never stored.</param>
    public AuraGrid(IMapQuery map)
    {
        _map = map;
    }

    public int GetTotalStacksAt(Vector3Int position, StatusEffectType effectType) =>
        _totalStacksByEffectType[(byte)effectType] is { } totals ? totals.GetValueOrDefault(position) : 0;

    public void AddSource(Vector3Int sourcePosition, int strength, StatusEffectType effectType) => Splat(sourcePosition, strength, effectType, sign: 1);

    public void RemoveSource(Vector3Int sourcePosition, int strength, StatusEffectType effectType) => Splat(sourcePosition, strength, effectType, sign: -1);

    private void Splat(Vector3Int sourcePosition, int strength, StatusEffectType effectType, int sign)
    {
        var totals = _totalStacksByEffectType[(byte)effectType] ??= new NeighborhoodCells<int>();

        DistanceFalloff.ScatterManhattan(sourcePosition, DistanceFalloff.MaxRadius(strength), strength, FalloffShape.Fading, _map.Bounds, (cellPosition, contribution) =>
        {
            var newTotal = totals.GetValueOrDefault(cellPosition) + sign * contribution;

            // Remove rather than store a zero -- keeps the store's size proportional to cells
            // actually under some source's influence right now, not to every cell any source has
            // ever touched.
            if (newTotal == 0)
            {
                totals.Remove(cellPosition);
            }
            else
            {
                totals.Set(cellPosition, newTotal);
            }
        });
    }
}
