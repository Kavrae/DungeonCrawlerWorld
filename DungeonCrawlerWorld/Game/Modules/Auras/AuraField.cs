using System.Runtime.InteropServices;
using Engine.Diagnostics;
using Engine.Events;
using Engine.Math;
using Engine.Tiles;
using Game.Floors;
using Game.Modules.Auras.Components;
using Game.Terrain;
using Game.World;

namespace Game.Modules.Auras;

/// <summary>Where every aura reaches: each cell's strongest power per aura, over every source.</summary>
/// <remarks>
/// <para>
/// The one structure that holds aura reach. Gameplay (AuraSystem) reads a cell's power for an
/// aura from it, and the map's glow (AuraView, blended in Presentation) is derived from the same values, so what is drawn
/// is what applies by construction.
/// </para>
/// <para>
/// <b>Strongest wins</b> (PLAN-aura-scaling.md, Decision 1): a cell holds the strongest single source's value there,
/// never a sum. Adding a source raises the cells it reaches. Removing or moving one clears each cell it was holding and
/// raises it again from the sources still reaching it: the entity sources (AuraSourceIndex) and the terrain around it,
/// read from the map. Field sources change rarely (terrain, stationary entities), so a removal pays that recompute.
/// </para>
/// <para>
/// Terrain auras are the field's own: terrain cells are not entities, so it scans the loaded terrain
/// once (EnsureBuilt, on first use -- startup population loads its neighborhoods without announcing
/// them) and then follows TerrainChangedEvent and TerrainLoadedEvent. A neighborhood that unloads is recomputed once it
/// is gone (NeighborhoodUnloadedEvent): it and the band its sources reached are cleared and raised again from every
/// source still there. Entity sources are added and removed by AuraSystem, which knows where each one is.
/// </para>
/// </remarks>
public sealed class AuraField
{
    private readonly IMapQuery _map;
    private readonly TerrainRegistry _terrain;
    private readonly AuraCatalog _auras;
    private readonly AuraGrid _auraGrid;
    private readonly HashSet<byte> _auraIdsEverInField = [];
    private readonly List<byte> _auraIdsInField = [];
    private readonly AuraSourceIndex _entitySources;

    /// <summary>For each aura id, the one terrain type radiating it (RadiatingTerrainTypeOf); null until first asked.</summary>
    private ushort[]? _radiatingTerrainTypeByAuraId;

    /// <summary>How many terrain types were registered when _radiatingTerrainTypeByAuraId was built: a type registered since rebuilds it.</summary>
    private int _radiatingTerrainTypesBuiltForCount;

    /// <summary>Each terrain type's aura, by type id, and the largest reach any terrain gives each aura; rebuilt when a terrain type is registered.</summary>
    private AuraSourceComponent?[] _terrainAuraByTypeId = [];
    private readonly int[] _maximumTerrainSizeByAuraId = new int[byte.MaxValue + 1];
    private int _terrainAurasBuiltForCount = -1;

    private readonly List<Vector3Int> _heldCellsScratch = [];
    private readonly List<(Vector3Int Position, ushort Power, byte Size)> _remainingSourcesScratch = [];

    private const ushort NoTerrainType = 0;
    private const ushort SeveralTerrainTypes = ushort.MaxValue;
    private bool _terrainScanned;

    public AuraField(IMapQuery map, TerrainRegistry terrain, AuraCatalog auras, EventBus eventBus)
    {
        _map = map;
        _terrain = terrain;
        _auras = auras;
        _auraGrid = new AuraGrid(map);
        _entitySources = new AuraSourceIndex(map.Bounds.Depth);

        eventBus.Subscribe<TerrainChangedEvent>(OnTerrainChanged);
        eventBus.Subscribe<TerrainLoadedEvent>(OnTerrainLoaded);
        eventBus.Subscribe<NeighborhoodUnloadedEvent>(OnNeighborhoodUnloaded);
    }

    /// <summary>Bumped every time a source is added or removed, so a cached rendering of the glow can tell whether anything changed since it was made.</summary>
    public int Version { get; private set; }

    /// <summary>How many tiles from itself a source reaches: its size.</summary>
    public static int ReachOf(AuraSourceComponent source) => source.Size;

    /// <summary>Every aura the field has held a source of -- the only auras that can reach anything. What each does and the colour it glows are read from the catalog by id, never kept here.</summary>
    public ReadOnlySpan<byte> AuraIdsInField => CollectionsMarshal.AsSpan(_auraIdsInField);

    /// <summary>A terrain cell at this position started radiating this source while the field was built.</summary>
    public event Action<Vector3Int, AuraSourceComponent>? TerrainAuraAdded;

    /// <summary>A terrain cell at this position stopped radiating this source while the field was built.</summary>
    public event Action<Vector3Int, AuraSourceComponent>? TerrainAuraRemoved;

    /// <summary>Scans the loaded terrain for auras the first time it is called; nothing after that.</summary>
    public void EnsureBuilt()
    {
        if (_terrainScanned)
        {
            return;
        }

        _terrainScanned = true;

        using (EngineHooks.DiagnosticScope("Aura Field Build"))
        {
            TerrainAuraSources.ForEach(_map, _terrain, _auras, AddSource);
        }
    }

    /// <summary>Chunks of per-cell values in use, across every aura.</summary>
    public int TotalsChunkCount => _auraGrid.TotalsChunkCount;

    /// <summary>Bytes the per-cell values hold, in use or kept for reuse.</summary>
    public long TotalsAllocatedBytes => _auraGrid.TotalsAllocatedBytes;

    /// <summary>Whether any aura reaches position at all -- an array read, for ruling a cell out before asking about each aura.</summary>
    public bool AnyAuraReaches(Vector3Int position) => _auraGrid.IsCovered(position);

    /// <summary>The aura's power at position: the strongest source's value there, by its falloff.</summary>
    public int GetTotalPowerAt(Vector3Int position, byte auraId) => _auraGrid.GetTotalPowerAt(position, auraId);

    /// <summary>Adds one source's reach around sourcePosition: every cell it reaches is raised to its value there.</summary>
    public void AddSource(Vector3Int sourcePosition, AuraSourceComponent source)
    {
        if (_auraIdsEverInField.Add(source.AuraId))
        {
            _auraIdsInField.Add(source.AuraId);
        }

        _auraGrid.RaiseSource(sourcePosition, source.Power, source.Size, FalloffOf(source.AuraId), source.AuraId);
        Version++;
    }

    /// <summary>Removes one source's reach from around sourcePosition, which must be where it was added: each cell it was holding is raised again from the sources still reaching it.</summary>
    /// <remarks>An entity source is taken out of AuraSourceIndex first (RemoveEntitySource), so it is not one of the sources still reaching.</remarks>
    public void RemoveSource(Vector3Int sourcePosition, AuraSourceComponent source) => RemoveSource(sourcePosition, source, stillHoldsWithinReachOf: null);

    /// <param name="stillHoldsWithinReachOf">Where the same source now stands, for a None source moved: the cells within its reach of there it still holds at the same value, so they are left as they are.</param>
    private void RemoveSource(Vector3Int sourcePosition, AuraSourceComponent source, Vector3Int? stillHoldsWithinReachOf)
    {
        var auraId = source.AuraId;
        var falloff = FalloffOf(auraId);
        _heldCellsScratch.Clear();
        _auraGrid.CollectHeldCells(sourcePosition, source.Power, source.Size, falloff, auraId, _heldCellsScratch, stillHoldsWithinReachOf);
        foreach (var cell in _heldCellsScratch)
        {
            _auraGrid.ClearCell(cell, auraId);
        }

        if (_heldCellsScratch.Count > 0)
        {
            var reach = source.Size;
            CollectRemainingSources(auraId, sourcePosition.X - reach, sourcePosition.Y - reach, sourcePosition.X + reach + 1, sourcePosition.Y + reach + 1, sourcePosition.Z);
            foreach (var (position, power, size) in _remainingSourcesScratch)
            {
                _auraGrid.RaiseCells(position, power, size, falloff, auraId, _heldCellsScratch);
            }
        }

        Version++;
    }

    /// <summary>The aura's falloff: its definition's, fixed for the session like the rest of it.</summary>
    private AuraFalloff FalloffOf(byte auraId) => _auras.Get(auraId).Falloff;

    /// <summary>Moves one source's reach from previousPosition, where it was added, to currentPosition.</summary>
    /// <remarks>A None source is full power to its edge, so where its old reach and new overlap its value is unchanged: only the cells that changed sides are written.</remarks>
    public void MoveSource(Vector3Int previousPosition, Vector3Int currentPosition, AuraSourceComponent source)
    {
        if (FalloffOf(source.AuraId) != AuraFalloff.None)
        {
            RemoveSource(previousPosition, source);
            AddSource(currentPosition, source);
            return;
        }

        RemoveSource(previousPosition, source, stillHoldsWithinReachOf: currentPosition);
        _auraGrid.RaiseSource(currentPosition, source.Power, source.Size, AuraFalloff.None, source.AuraId, excludedCenter: previousPosition);
        Version++;
    }

    /// <summary>Adds an entity's source around sourcePosition, credited to attribution: the entity, or for an anchor whoever placed it.</summary>
    public void AddEntitySource(int entityId, Vector3Int sourcePosition, AuraSourceComponent source, ActionSource attribution)
    {
        _entitySources.Add(entityId, sourcePosition, source, attribution);
        AddSource(sourcePosition, source);
    }

    /// <summary>Removes an entity's source from around sourcePosition, where it was added.</summary>
    public void RemoveEntitySource(int entityId, Vector3Int sourcePosition, AuraSourceComponent source)
    {
        _entitySources.Remove(entityId, sourcePosition, source);
        RemoveSource(sourcePosition, source);
    }

    /// <summary>Moves an entity's source from previousPosition, where it was added, to currentPosition, keeping whom it is credited to.</summary>
    /// <remarks>The index moves it first, so the cells it held where it was are raised again from it where it is now as well as from every other source.</remarks>
    public void MoveEntitySource(int entityId, Vector3Int previousPosition, Vector3Int currentPosition, AuraSourceComponent source)
    {
        _entitySources.Move(entityId, previousPosition, currentPosition, source);
        MoveSource(previousPosition, currentPosition, source);
    }

    /// <summary>The aura's power reaching entityId at position -- the strongest source there other than the ones the entity carries -- for whether it is exposed and how strongly.</summary>
    /// <remarks>A source never affects the entity carrying it. An aura it set down on a tile does reach it: one that shouldn't grants it an immunity first.</remarks>
    public int PowerReaching(Vector3Int position, byte auraId, int entityId)
    {
        var total = GetTotalPowerAt(position, auraId);
        if (total <= 0 || !_entitySources.HasEntitySources(auraId))
        {
            return Math.Max(0, total);
        }

        var hasOther = _entitySources.TryFindStrongest(auraId, position, entityId, FalloffOf(auraId), out _, out var strongestValue, out var ownValue);
        return ownValue < total ? total : Math.Max(hasOther ? strongestValue : 0, TerrainValueAt(position, auraId));
    }

    /// <summary>Whom the aura's effect reaching entityId at position is credited to: the strongest single contributor.</summary>
    /// <remarks>
    /// Credit only: the effect still has no source entity. The contributors are each entity source
    /// other than the ones the entity carries, credited to its entity or an anchor's placer (the placer included), and the
    /// terrain. An entity source wins a tie with the terrain, and ties between entity sources go to the lowest key. The
    /// terrain is credited to its terrain type, or to the aura itself when several terrain types radiate it, or when
    /// nothing contributes.
    /// </remarks>
    public ActionSource Attribute(Vector3Int position, byte auraId, int entityId)
    {
        PowerReaching(position, auraId, entityId, out var credit);
        return credit;
    }

    /// <summary>PowerReaching and Attribute from one look at the sources: the power reaching entityId, and whom it is credited to.</summary>
    /// <remarks>
    /// The cell holds the strongest value over every source. When it is the entity's own that holds it, the strongest of
    /// the others is the strongest other entity source or the terrain's value there, read from the map -- the only
    /// case that reads terrain, since anywhere else the cell's value already says.
    /// </remarks>
    public int PowerReaching(Vector3Int position, byte auraId, int entityId, out ActionSource credit)
    {
        var total = GetTotalPowerAt(position, auraId);
        if (total <= 0)
        {
            credit = ActionSource.FromAura(auraId);
            return 0;
        }

        if (!_entitySources.HasEntitySources(auraId))
        {
            credit = TerrainCreditOf(auraId);
            return total;
        }

        var hasOther = _entitySources.TryFindStrongest(auraId, position, entityId, FalloffOf(auraId), out var strongest, out var strongestValue, out var ownValue);
        if (ownValue < total)
        {
            credit = hasOther && strongestValue >= total ? strongest : TerrainCreditOf(auraId);
            return total;
        }

        var terrainValue = TerrainValueAt(position, auraId);
        credit = hasOther && strongestValue >= terrainValue ? strongest : terrainValue > 0 ? TerrainCreditOf(auraId) : ActionSource.FromAura(auraId);
        return Math.Max(hasOther ? strongestValue : 0, terrainValue);
    }

    /// <summary>The terrain's credit for an aura: its one radiating terrain type, or the aura itself when several or none radiate it.</summary>
    private ActionSource TerrainCreditOf(byte auraId) =>
        RadiatingTerrainTypeOf(auraId) is var terrainTypeId && terrainTypeId != NoTerrainType && terrainTypeId != SeveralTerrainTypes
            ? ActionSource.FromTerrain(terrainTypeId)
            : ActionSource.FromAura(auraId);

    /// <summary>The one terrain type radiating auraId, NoTerrainType for none, SeveralTerrainTypes for more than one.</summary>
    private ushort RadiatingTerrainTypeOf(byte auraId)
    {
        if (_radiatingTerrainTypeByAuraId is not { } byAuraId || _radiatingTerrainTypesBuiltForCount != _terrain.Count)
        {
            byAuraId = new ushort[byte.MaxValue + 1];
            for (var typeId = 1; typeId <= _terrain.Count; typeId++)
            {
                if (TerrainAuraSources.TryGetAura(_terrain, _auras, (ushort)typeId, out var aura))
                {
                    byAuraId[aura.AuraId] = byAuraId[aura.AuraId] == NoTerrainType ? (ushort)typeId : SeveralTerrainTypes;
                }
            }

            _radiatingTerrainTypeByAuraId = byAuraId;
            _radiatingTerrainTypesBuiltForCount = _terrain.Count;
        }

        return byAuraId[auraId];
    }

    /// <summary>The strongest value any terrain gives the aura at position, read from the map around it.</summary>
    private int TerrainValueAt(Vector3Int position, byte auraId)
    {
        var reach = MaximumTerrainSizeOf(auraId);
        if (reach == 0)
        {
            return 0;
        }

        _remainingSourcesScratch.Clear();
        CollectTerrainSources(auraId, position.X - reach, position.Y - reach, position.X + reach + 1, position.Y + reach + 1, position.Z);
        var falloff = FalloffOf(auraId);
        var strongest = 0;
        foreach (var (sourcePosition, power, size) in _remainingSourcesScratch)
        {
            var distance = GridDistance.ManhattanDistance(sourcePosition, position);
            if (distance <= size)
            {
                strongest = Math.Max(strongest, falloff.ValueAt(power, size, distance));
            }
        }

        return strongest;
    }

    /// <summary>Fills _remainingSourcesScratch with every field source of auraId that may reach a cell of the rectangle on layer: entity sources from the index, terrain from the map.</summary>
    private void CollectRemainingSources(byte auraId, int minX, int minY, int maxXExclusive, int maxYExclusive, int layer)
    {
        _remainingSourcesScratch.Clear();
        var entityReach = _entitySources.MaximumSizeOf(auraId);
        if (entityReach > 0)
        {
            _entitySources.CollectIn(auraId, minX - entityReach, minY - entityReach, maxXExclusive + entityReach, maxYExclusive + entityReach, _remainingSourcesScratch);
        }

        var terrainReach = MaximumTerrainSizeOf(auraId);
        if (terrainReach > 0)
        {
            CollectTerrainSources(auraId, minX - terrainReach, minY - terrainReach, maxXExclusive + terrainReach, maxYExclusive + terrainReach, layer);
        }
    }

    /// <summary>Adds to _remainingSourcesScratch every loaded terrain cell radiating auraId in the rectangle, on layer, or on every layer for a negative one.</summary>
    private void CollectTerrainSources(byte auraId, int minX, int minY, int maxXExclusive, int maxYExclusive, int layer)
    {
        var terrainAuras = TerrainAurasByTypeId();
        var bounds = _map.Bounds;
        var firstLayer = layer < 0 ? 0 : layer;
        var lastLayer = layer < 0 ? bounds.Depth - 1 : layer;
        minX = Math.Max(minX, bounds.MinX);
        minY = Math.Max(minY, bounds.MinY);
        maxXExclusive = Math.Min(maxXExclusive, bounds.MaxX);
        maxYExclusive = Math.Min(maxYExclusive, bounds.MaxY);

        for (var z = firstLayer; z <= lastLayer; z++)
        {
            if (Map.TerrainLayerFor(z) is null)
            {
                continue;
            }

            for (var y = minY; y < maxYExclusive; y++)
            {
                for (var x = minX; x < maxXExclusive; x++)
                {
                    var position = new Vector3Int(x, y, z);
                    if (!_map.IsOnMap(position))
                    {
                        x = Math.Min(maxXExclusive, Neighborhoods.OriginOf(Neighborhoods.CellOf(x) + 1)) - 1;
                        continue;
                    }

                    var typeId = _map.GetTerrainAt(position).TypeId;
                    if (typeId < terrainAuras.Length && terrainAuras[typeId] is { } aura && aura.AuraId == auraId)
                    {
                        _remainingSourcesScratch.Add((position, aura.Power, aura.Size));
                    }
                }
            }
        }
    }

    /// <summary>The largest reach any terrain type gives auraId, or 0 when no terrain radiates it.</summary>
    private int MaximumTerrainSizeOf(byte auraId)
    {
        TerrainAurasByTypeId();
        return _maximumTerrainSizeByAuraId[auraId];
    }

    private AuraSourceComponent?[] TerrainAurasByTypeId()
    {
        if (_terrainAurasBuiltForCount == _terrain.Count)
        {
            return _terrainAuraByTypeId;
        }

        var byTypeId = new AuraSourceComponent?[_terrain.Count + 1];
        Array.Clear(_maximumTerrainSizeByAuraId);
        for (var typeId = 1; typeId <= _terrain.Count; typeId++)
        {
            if (TerrainAuraSources.TryGetAura(_terrain, _auras, (ushort)typeId, out var aura))
            {
                byTypeId[typeId] = aura;
                _maximumTerrainSizeByAuraId[aura.AuraId] = Math.Max(_maximumTerrainSizeByAuraId[aura.AuraId], aura.Size);
            }
        }

        _terrainAuraByTypeId = byTypeId;
        _terrainAurasBuiltForCount = _terrain.Count;
        return byTypeId;
    }

    /// <summary>Swaps a changed cell's terrain aura: the previous terrain's out, the new terrain's in. Before the terrain is scanned there is nothing to update; the scan will see the new terrain.</summary>
    private void OnTerrainChanged(TerrainChangedEvent changed)
    {
        if (!_terrainScanned)
        {
            return;
        }

        var position = new Vector3Int(changed.X, changed.Y, (int)changed.TerrainLayer);

        if (TerrainAuraSources.TryGetAura(_terrain, _auras, changed.PreviousTypeId, out var previousAura))
        {
            RemoveSource(position, previousAura);
            TerrainAuraRemoved?.Invoke(position, previousAura);
        }

        if (TerrainAuraSources.TryGetAura(_terrain, _auras, changed.TypeId, out var aura))
        {
            AddSource(position, aura);
            TerrainAuraAdded?.Invoke(position, aura);
        }
    }

    /// <summary>Adds a newly loaded area's terrain auras, from the list made when the neighborhood was planned.</summary>
    private void OnTerrainLoaded(TerrainLoadedEvent loaded)
    {
        if (!_terrainScanned)
        {
            return;
        }

        foreach (var auraCell in loaded.AuraCells)
        {
            AddSource(auraCell.Position, auraCell.Aura);
        }
    }

    /// <summary>Recomputes what an unloaded neighborhood's terrain reached: it and the band around it, one reach wide, are cleared and raised again from every source still there.</summary>
    /// <remarks>
    /// Runs once the neighborhood is off the map, so its own terrain is no longer read. Its entities' sources left with
    /// them. A source outside it still writes its reach into it, as it does into any unloaded neighbor.
    /// </remarks>
    private void OnNeighborhoodUnloaded(NeighborhoodUnloadedEvent unloaded)
    {
        if (!_terrainScanned)
        {
            return;
        }

        var reach = 0;
        foreach (var auraId in _auraGrid.AuraIdsWithTotals)
        {
            reach = Math.Max(reach, Math.Max(_entitySources.MaximumSizeOf(auraId), MaximumTerrainSizeOf(auraId)));
        }

        var minX = Neighborhoods.OriginOf(unloaded.CellX);
        var minY = Neighborhoods.OriginOf(unloaded.CellY);
        var maxX = minX + Neighborhoods.SizeTiles;
        var maxY = minY + Neighborhoods.SizeTiles;
        var depth = _map.Bounds.Depth;

        _auraGrid.ClearNeighborhood(unloaded.CellX, unloaded.CellY);
        _auraGrid.ClearArea(new MapBounds(minX - reach, minY - reach, maxX + reach, minY, depth));
        _auraGrid.ClearArea(new MapBounds(minX - reach, maxY, maxX + reach, maxY + reach, depth));
        _auraGrid.ClearArea(new MapBounds(minX - reach, minY, minX, maxY, depth));
        _auraGrid.ClearArea(new MapBounds(maxX, minY, maxX + reach, maxY, depth));

        foreach (var auraId in _auraIdsInField)
        {
            CollectRemainingSources(auraId, minX - reach, minY - reach, maxX + reach, maxY + reach, layer: -1);
            var falloff = FalloffOf(auraId);
            foreach (var (position, power, size) in _remainingSourcesScratch)
            {
                _auraGrid.RaiseSource(position, power, size, falloff, auraId);
            }
        }

        Version++;
    }
}
