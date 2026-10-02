using System.Runtime.InteropServices;
using Engine.Diagnostics;
using Engine.Events;
using Engine.Math;
using Game.Modules.Auras.Components;
using Game.Terrain;
using Game.World;
using Microsoft.Xna.Framework;

namespace Game.Modules.Auras;

/// <summary>Where every aura reaches: each cell's total strength per aura, summed over every source, and the glow that follows from it.</summary>
/// <remarks>
/// <para>
/// The one structure that holds aura reach. Gameplay (AuraSystem) reads a cell's strength for an
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
        auras.DefinitionChanged += _ => Version++;
    }

    /// <summary>Bumped every time a source is added or removed or an aura's definition is replaced, so a cached rendering of the glow can tell whether anything changed since it was made.</summary>
    public int Version { get; private set; }

    /// <summary>The reach of the strongest source the field has held: how far from a changed source an occupant can be and still be affected by it.</summary>
    public int MaxScanRadius { get; private set; }

    /// <summary>Every aura the field has held a source of -- the only auras that can reach anything. What each does and the colour it glows are read from the catalog by id, never kept here.</summary>
    public ReadOnlySpan<byte> AuraIdsInField => CollectionsMarshal.AsSpan(_auraIdsInField);

    /// <summary>A terrain cell at this position started radiating an aura while the field was built.</summary>
    public event Action<Vector3Int>? TerrainAuraAdded;

    /// <summary>A terrain cell at this position stopped radiating an aura while the field was built.</summary>
    public event Action<Vector3Int>? TerrainAuraRemoved;

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

    /// <summary>Calls visit with the position of every loaded terrain cell radiating auraId.</summary>
    public void ForEachTerrainSource(byte auraId, Action<Vector3Int> visit) =>
        TerrainAuraSources.ForEach(_map, _terrain, _auras, (position, source) =>
        {
            if (source.AuraId == auraId)
            {
                visit(position);
            }
        });

    /// <summary>Whether any aura reaches position at all -- an array read, for ruling a cell out before asking about each aura.</summary>
    public bool AnyAuraReaches(Vector3Int position) => _auraGrid.IsCovered(position);

    /// <summary>The aura's total strength at position: every source's falloff added together.</summary>
    public int GetTotalStrengthAt(Vector3Int position, byte auraId) => _auraGrid.GetTotalStrengthAt(position, auraId);

    /// <summary>Adds one source's reach around sourcePosition.</summary>
    public void AddSource(Vector3Int sourcePosition, AuraSourceComponent source)
    {
        if (_auraIdsEverInField.Add(source.AuraId))
        {
            _auraIdsInField.Add(source.AuraId);
        }

        _auraGrid.AddSource(sourcePosition, source.Strength, source.AuraId);
        MaxScanRadius = Math.Max(MaxScanRadius, DistanceFalloff.MaxRadius(source.Strength));
        Version++;
    }

    /// <summary>Removes one source's reach from around sourcePosition, which must be where it was added.</summary>
    public void RemoveSource(Vector3Int sourcePosition, AuraSourceComponent source)
    {
        _auraGrid.RemoveSource(sourcePosition, source.Strength, source.AuraId);
        Version++;
    }

    /// <summary>The glow at position: the colours of the auras reaching it, averaged by each one's strength there, and their summed strength.</summary>
    /// <remarks>False where no aura reaches. Two auras overlapping -- from one source or several -- blend toward whichever is stronger at the cell.</remarks>
    public bool TryGetGlow(Vector3Int position, out Color glowColor, out int totalStrength)
    {
        EnsureBuilt();

        if (!_auraGrid.IsCovered(position))
        {
            glowColor = default;
            totalStrength = 0;
            return false;
        }

        var red = 0f;
        var green = 0f;
        var blue = 0f;
        totalStrength = 0;

        foreach (var auraId in _auraIdsInField)
        {
            var strength = _auraGrid.GetTotalStrengthAt(position, auraId);
            if (strength <= 0)
            {
                continue;
            }

            var auraGlowColor = _auras.Get(auraId).GlowColor;
            red += auraGlowColor.R * strength;
            green += auraGlowColor.G * strength;
            blue += auraGlowColor.B * strength;
            totalStrength += strength;
        }

        if (totalStrength <= 0)
        {
            glowColor = default;
            return false;
        }

        glowColor = new Color((byte)(red / totalStrength), (byte)(green / totalStrength), (byte)(blue / totalStrength));
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
            TerrainAuraRemoved?.Invoke(position);
        }

        if (TerrainAuraSources.TryGetAura(_terrain, _auras, changed.TypeId, out var aura))
        {
            AddSource(position, aura);
            TerrainAuraAdded?.Invoke(position);
        }
    }

    /// <summary>A terrain's definition was replaced: if the aura it radiates changed, every loaded cell of that terrain swaps the old one for the new.</summary>
    private void OnTerrainDefinitionChanged(ushort terrainTypeId, TerrainDefinition previous, TerrainDefinition current)
    {
        if (!_terrainScanned || previous.Aura == current.Aura)
        {
            return;
        }

        TerrainCells.ForEachOfType(_map, terrainTypeId, position =>
        {
            if (previous.Aura is { } previousAura)
            {
                RemoveSource(position, new AuraSourceComponent(_auras.Register(previousAura.Aura), previousAura.Strength));
                TerrainAuraRemoved?.Invoke(position);
            }

            if (current.Aura is { } currentAura)
            {
                AddSource(position, new AuraSourceComponent(_auras.Register(currentAura.Aura), currentAura.Strength));
                TerrainAuraAdded?.Invoke(position);
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
