using System.Runtime.InteropServices;
using Engine.Diagnostics;
using Engine.Events;
using Engine.Math;
using Game.Modules.Auras.Components;
using Game.Terrain;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Modules.Auras;

/// <summary>Where every aura reaches: each cell's total power per aura, summed over every source, and the glow that follows from it.</summary>
/// <remarks>
/// <para>
/// The one structure that holds aura reach. Gameplay (AuraSystem) reads a cell's power for an
/// aura from it, and the map's glow (AuraGlowView) is derived from the same totals, so what is drawn
/// is what applies by construction.
/// </para>
/// <para>
/// Terrain auras are the field's own: terrain cells are not entities, so it scans the loaded terrain
/// once (EnsureBuilt, on first use -- startup population loads its neighborhoods without announcing
/// them) and then follows TerrainChangedEvent, TerrainLoadedEvent, TerrainUnloadingEvent and a
/// terrain definition being replaced (TerrainRegistry.DefinitionChanged). Entity
/// sources are added and removed by AuraSystem, which knows where each one is.
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
    private readonly AuraFalloff[] _falloffInFieldByAuraId = new AuraFalloff[byte.MaxValue + 1];
    private readonly int[] _sourceCountByAuraId = new int[byte.MaxValue + 1];
    private readonly AuraSourceIndex _entitySources = new();

    /// <summary>For each aura id, the one terrain type radiating it (RadiatingTerrainTypeOf); null until first asked and after any terrain or aura definition changes.</summary>
    private ushort[]? _radiatingTerrainTypeByAuraId;

    /// <summary>How many terrain types were registered when _radiatingTerrainTypeByAuraId was built: a type registered since rebuilds it.</summary>
    private int _radiatingTerrainTypesBuiltForCount;

    private const ushort NoTerrainType = 0;
    private const ushort SeveralTerrainTypes = ushort.MaxValue;
    private bool _terrainScanned;

    public AuraField(IMapQuery map, TerrainRegistry terrain, AuraCatalog auras, EventBus eventBus)
    {
        _map = map;
        _terrain = terrain;
        _auras = auras;
        _auraGrid = new AuraGrid(map);

        eventBus.Subscribe<TerrainChangedEvent>(OnTerrainChanged);
        eventBus.Subscribe<TerrainLoadedEvent>(OnTerrainLoaded);
        eventBus.Subscribe<TerrainUnloadingEvent>(OnTerrainUnloading);
        terrain.DefinitionChanged += OnTerrainDefinitionChanged;
        auras.DefinitionChanged += _ =>
        {
            Version++;
            _radiatingTerrainTypeByAuraId = null;
        };
    }

    /// <summary>Bumped every time a source is added or removed or an aura's definition is replaced, so a cached rendering of the glow can tell whether anything changed since it was made.</summary>
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

    /// <summary>Calls visit with the position and source of every loaded terrain cell radiating auraId.</summary>
    public void ForEachTerrainSource(byte auraId, Action<Vector3Int, AuraSourceComponent> visit) =>
        TerrainAuraSources.ForEach(_map, _terrain, _auras, (position, source) =>
        {
            if (source.AuraId == auraId)
            {
                visit(position, source);
            }
        });

    /// <summary>Chunks of per-cell totals in use, across every aura.</summary>
    public int TotalsChunkCount => _auraGrid.TotalsChunkCount;

    /// <summary>Bytes the per-cell totals hold, in use or kept for reuse.</summary>
    public long TotalsAllocatedBytes => _auraGrid.TotalsAllocatedBytes;

    /// <summary>Whether any aura reaches position at all -- an array read, for ruling a cell out before asking about each aura.</summary>
    public bool AnyAuraReaches(Vector3Int position) => _auraGrid.IsCovered(position);

    /// <summary>The aura's total power at position: every source's value there, by its falloff, added together.</summary>
    public int GetTotalPowerAt(Vector3Int position, byte auraId) => _auraGrid.GetTotalPowerAt(position, auraId);

    /// <summary>Adds one source's reach around sourcePosition.</summary>
    /// <remarks>
    /// The first source of an aura in the field fixes the falloff its sources are written with, and
    /// it holds until the field has no source of that aura left: every source is taken out with the
    /// falloff it was put in with, so a definition replaced with a different falloff while sources of
    /// it are out takes effect when they have all gone.
    /// </remarks>
    public void AddSource(Vector3Int sourcePosition, AuraSourceComponent source)
    {
        if (_auraIdsEverInField.Add(source.AuraId))
        {
            _auraIdsInField.Add(source.AuraId);
        }

        if (_sourceCountByAuraId[source.AuraId]++ == 0)
        {
            _falloffInFieldByAuraId[source.AuraId] = _auras.Get(source.AuraId).Falloff;
        }

        _auraGrid.AddSource(sourcePosition, source.Power, source.Size, _falloffInFieldByAuraId[source.AuraId], source.AuraId);
        Version++;
    }

    /// <summary>Removes one source's reach from around sourcePosition, which must be where it was added.</summary>
    public void RemoveSource(Vector3Int sourcePosition, AuraSourceComponent source)
    {
        _auraGrid.RemoveSource(sourcePosition, source.Power, source.Size, _falloffInFieldByAuraId[source.AuraId], source.AuraId);
        _sourceCountByAuraId[source.AuraId]--;
        Version++;
    }

    /// <summary>Moves one source's reach from previousPosition, where it was added, to currentPosition.</summary>
    /// <remarks>Writes only the cells that changed sides for a None aura (see AuraGrid.MoveSource).</remarks>
    public void MoveSource(Vector3Int previousPosition, Vector3Int currentPosition, AuraSourceComponent source)
    {
        _auraGrid.MoveSource(previousPosition, currentPosition, source.Power, source.Size, _falloffInFieldByAuraId[source.AuraId], source.AuraId);
        Version++;
    }

    /// <summary>Adds an entity's source around sourcePosition, credited to attribution: the entity, or for an anchor whoever placed it.</summary>
    public void AddEntitySource(int entityId, Vector3Int sourcePosition, AuraSourceComponent source, ActionSource attribution)
    {
        AddSource(sourcePosition, source);
        _entitySources.Add(entityId, sourcePosition, source, attribution);
    }

    /// <summary>Removes an entity's source from around sourcePosition, where it was added.</summary>
    public void RemoveEntitySource(int entityId, Vector3Int sourcePosition, AuraSourceComponent source)
    {
        RemoveSource(sourcePosition, source);
        _entitySources.Remove(entityId, sourcePosition, source);
    }

    /// <summary>Moves an entity's source from previousPosition, where it was added, to currentPosition, keeping whom it is credited to.</summary>
    public void MoveEntitySource(int entityId, Vector3Int previousPosition, Vector3Int currentPosition, AuraSourceComponent source)
    {
        MoveSource(previousPosition, currentPosition, source);
        _entitySources.Move(entityId, previousPosition, currentPosition, source);
    }

    /// <summary>The aura's power reaching entityId at position -- the total there minus the sources the entity carries -- for whether it is exposed and how strongly.</summary>
    /// <remarks>A source never affects the entity carrying it. An aura it set down on a tile does reach it: one that shouldn't grants it an immunity first.</remarks>
    public int PowerReaching(Vector3Int position, byte auraId, int entityId)
    {
        var total = GetTotalPowerAt(position, auraId);
        if (total <= 0 || !_entitySources.HasEntitySources(auraId))
        {
            return Math.Max(0, total);
        }

        _entitySources.TryFindStrongest(auraId, position, entityId, _falloffInFieldByAuraId[auraId], out _, out _, out _, out var ownTotal);
        return Math.Max(0, total - ownTotal);
    }

    /// <summary>Whom the aura's effect reaching entityId at position is credited to: the strongest single contributor.</summary>
    /// <remarks>
    /// Credit only: the effect still has no source entity. The contributors are each entity source
    /// other than the ones the entity carries, credited to its entity or an anchor's placer (the placer included), and the
    /// terrain as one share -- the cell's total minus every entity source's value there. An entity source
    /// wins a tie with the terrain, and ties between entity sources go to the lowest key. The terrain's
    /// share is credited to its terrain type, or to the aura itself when several terrain types radiate it,
    /// or when nothing contributes.
    /// </remarks>
    public ActionSource Attribute(Vector3Int position, byte auraId, int entityId)
    {
        PowerReaching(position, auraId, entityId, out var credit);
        return credit;
    }

    /// <summary>PowerReaching and Attribute from one look at the sources: the power reaching entityId, and whom it is credited to.</summary>
    public int PowerReaching(Vector3Int position, byte auraId, int entityId, out ActionSource credit)
    {
        var total = GetTotalPowerAt(position, auraId);
        var hasEntityContributor = _entitySources.TryFindStrongest(auraId, position, entityId, _falloffInFieldByAuraId[auraId], out var strongest, out var strongestValue, out var entityTotal, out var ownTotal);
        credit = CreditOf(auraId, total - entityTotal, hasEntityContributor, strongest, strongestValue);
        return Math.Max(0, total - ownTotal);
    }

    private ActionSource CreditOf(byte auraId, int terrainShare, bool hasEntityContributor, ActionSource strongest, int strongestValue)
    {

        if (hasEntityContributor && strongestValue >= terrainShare)
        {
            return strongest;
        }

        return terrainShare > 0 && RadiatingTerrainTypeOf(auraId) is var terrainTypeId && terrainTypeId != NoTerrainType && terrainTypeId != SeveralTerrainTypes
            ? ActionSource.FromTerrain(terrainTypeId)
            : ActionSource.FromAura(auraId);
    }

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

    /// <summary>The glow at position: the colours of the auras reaching it, averaged by each one's power there, and their summed power.</summary>
    /// <remarks>False where no aura reaches. Two auras overlapping -- from one source or several -- blend toward whichever is stronger at the cell.</remarks>
    public bool TryGetGlow(Vector3Int position, out Color glowColor, out int totalPower)
    {
        EnsureBuilt();

        if (!_auraGrid.IsCovered(position))
        {
            glowColor = default;
            totalPower = 0;
            return false;
        }

        var red = 0f;
        var green = 0f;
        var blue = 0f;
        totalPower = 0;

        foreach (var auraId in _auraIdsInField)
        {
            var power = _auraGrid.GetTotalPowerAt(position, auraId);
            if (power <= 0)
            {
                continue;
            }

            var auraGlowColor = _auras.Get(auraId).GlowColor;
            red += auraGlowColor.R * power;
            green += auraGlowColor.G * power;
            blue += auraGlowColor.B * power;
            totalPower += power;
        }

        if (totalPower <= 0)
        {
            glowColor = default;
            return false;
        }

        glowColor = new Color((byte)(red / totalPower), (byte)(green / totalPower), (byte)(blue / totalPower));
        return true;
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

    /// <summary>A terrain's definition was replaced: if the aura it radiates changed, every loaded cell of that terrain swaps the old one for the new.</summary>
    private void OnTerrainDefinitionChanged(ushort terrainTypeId, TerrainDefinition previous, TerrainDefinition current)
    {
        _radiatingTerrainTypeByAuraId = null;

        if (!_terrainScanned || previous.Aura == current.Aura)
        {
            return;
        }

        TerrainCells.ForEachOfType(_map, terrainTypeId, position =>
        {
            if (previous.Aura is { } previousAura)
            {
                var previousSource = new AuraSourceComponent(_auras.Register(previousAura.Aura), previousAura.Power, previousAura.Size);
                RemoveSource(position, previousSource);
                TerrainAuraRemoved?.Invoke(position, previousSource);
            }

            if (current.Aura is { } currentAura)
            {
                var currentSource = new AuraSourceComponent(_auras.Register(currentAura.Aura), currentAura.Power, currentAura.Size);
                AddSource(position, currentSource);
                TerrainAuraAdded?.Invoke(position, currentSource);
            }
        });
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

    /// <summary>Removes an unloading area's terrain auras, which reach into loaded neighbors.</summary>
    private void OnTerrainUnloading(TerrainUnloadingEvent unloading)
    {
        if (_terrainScanned)
        {
            TerrainAuraSources.ForEach(_map, _terrain, _auras, unloading.Area, RemoveSource);
        }
    }
}
