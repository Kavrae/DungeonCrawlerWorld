using Engine.Math;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>
/// Precomputed per-cell, per-aura total strength across the whole map -- a sparse index for every
/// aura at once.
///
/// Kept in sync incrementally as sources are added and removed (AddSource/RemoveSource), each
/// scattering or unscattering its falloff.
///
/// One NeighborhoodCells per aura, indexed by the aura's id, so a lookup is an array read plus a
/// per-neighborhood dictionary lookup, and a neighborhood's totals can be dropped with it. Beside
/// them, one bit per cell says whether any aura reaches it at all (IsCovered): most cells are in no
/// aura, and most questions are asked of those, so the common answer costs an array read instead
/// of a hash lookup per aura.
///
/// Uses Manhattan distance (diamond-shaped falloff).
/// </summary>
/// <remarks>
/// A source's reach is clipped only to a bounded map's declared rectangle, which never changes. On
/// an unbounded map nothing is clipped: Bounds there follows the loaded neighborhoods, and a source
/// added under one rectangle and removed under another would leave totals behind. So a source at
/// the window's edge writes its reach into the unloaded neighbor's cells, where nothing reads it
/// until that neighbor loads -- with the reach already in place.
/// </remarks>
public sealed class AuraGrid
{
    private const int Unlimited = int.MaxValue;

    private readonly NeighborhoodCells<int>?[] _totalStrengthByAuraId = new NeighborhoodCells<int>?[byte.MaxValue + 1];
    private readonly List<byte> _auraIdsWithTotals = [];
    private readonly NeighborhoodBits _coveredCells;
    private readonly IMapQuery _map;

    /// <param name="map">Read for its depth, and for a bounded map's rectangle, past which no contribution is stored.</param>
    public AuraGrid(IMapQuery map)
    {
        _map = map;
        _coveredCells = new NeighborhoodBits(map.Bounds.Depth);
    }

    /// <summary>The rectangle a source's reach is clipped to: a bounded map's own, and none at all on an unbounded one.</summary>
    private MapBounds ReachLimits =>
        _map.IsBounded ? _map.Bounds : new MapBounds(-Unlimited, -Unlimited, Unlimited, Unlimited, _map.Bounds.Depth);

    /// <summary>Whether any aura reaches position. False means GetTotalStrengthAt is zero there for every aura.</summary>
    public bool IsCovered(Vector3Int position) => _coveredCells.IsSet(position);

    public int GetTotalStrengthAt(Vector3Int position, byte auraId) =>
        _totalStrengthByAuraId[auraId] is { } totals ? totals.GetValueOrDefault(position) : 0;

    public void AddSource(Vector3Int sourcePosition, int strength, byte auraId) => Splat(sourcePosition, strength, auraId, sign: 1);

    public void RemoveSource(Vector3Int sourcePosition, int strength, byte auraId) => Splat(sourcePosition, strength, auraId, sign: -1);

    private void Splat(Vector3Int sourcePosition, int strength, byte auraId, int sign)
    {
        var totals = _totalStrengthByAuraId[auraId];
        if (totals is null)
        {
            totals = new NeighborhoodCells<int>();
            _totalStrengthByAuraId[auraId] = totals;
            _auraIdsWithTotals.Add(auraId);
        }

        DistanceFalloff.ScatterManhattan(sourcePosition, DistanceFalloff.MaxRadius(strength), strength, FalloffShape.Fading, ReachLimits, (cellPosition, contribution) =>
        {
            var newTotal = totals.GetValueOrDefault(cellPosition) + sign * contribution;

            // Remove rather than store a zero -- keeps the store's size proportional to cells
            // actually under some source's influence right now, not to every cell any source has
            // ever touched.
            if (newTotal == 0)
            {
                totals.Remove(cellPosition);
                if (!AnyOtherAuraReaches(cellPosition, auraId))
                {
                    _coveredCells.Clear(cellPosition);
                }
            }
            else
            {
                totals.Set(cellPosition, newTotal);
                _coveredCells.Set(cellPosition);
            }
        });
    }

    private bool AnyOtherAuraReaches(Vector3Int position, byte exceptAuraId)
    {
        foreach (var auraId in _auraIdsWithTotals)
        {
            if (auraId != exceptAuraId && _totalStrengthByAuraId[auraId]!.TryGetValue(position, out _))
            {
                return true;
            }
        }

        return false;
    }
}
