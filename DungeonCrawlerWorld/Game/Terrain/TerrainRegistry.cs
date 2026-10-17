using System.Diagnostics.CodeAnalysis;

namespace Game.Terrain;

/// <summary>Every terrain definition in the session, by runtime id and by key.</summary>
/// <remarks>
/// Filled during IGameModule.Configure (TerrainModule for the built-ins, and any mod's own), before
/// population and before any system reads it -- the same ordering StatusEffectApplierRegistry
/// relies on. Id 0 is reserved for "no terrain", so a zeroed cell array means an empty map.
/// </remarks>
public sealed class TerrainRegistry
{
    /// <summary>The TypeId of a cell with no terrain.</summary>
    public const ushort None = 0;

    private readonly List<TerrainDefinition?> _definitions = [null];
    private readonly Dictionary<string, ushort> _idsByKey = [];

    /// <summary>Each id's link group as an int (see GetLinkGroup); 0 until first asked for.</summary>
    private readonly List<int> _linkGroups = [0];

    private readonly Dictionary<string, int> _linkGroupIdsByName = [];

    /// <summary>Each id's BlocksMovement, flattened out of its definition for the movement hot path.</summary>
    private readonly List<bool> _blocksMovement = [false];

    /// <summary>A registered key was registered again: its id, the definition it held, and the one that replaced it.</summary>
    /// <remarks>For what is derived from a definition and held outside it -- the aura field's reach, an entity's exposure to a contact -- to follow a definition replaced during a session. Nothing subscribes during Configure, when a mod overriding a built-in is the usual cause.</remarks>
    public event Action<ushort, TerrainDefinition, TerrainDefinition>? DefinitionChanged;

    /// <summary>Registers definition and returns its runtime id. Registering a key again replaces that definition in place and keeps its id, so a mod can override a built-in, and raises DefinitionChanged.</summary>
    public ushort Register(TerrainDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (_idsByKey.TryGetValue(definition.Key, out var existingId))
        {
            var previous = _definitions[existingId]!;
            _definitions[existingId] = definition;
            _linkGroups[existingId] = 0;
            _blocksMovement[existingId] = definition.BlocksMovement;
            DefinitionChanged?.Invoke(existingId, previous, definition);
            return existingId;
        }

        if (_definitions.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException($"More than {ushort.MaxValue} terrain definitions registered.");
        }

        var id = (ushort)_definitions.Count;
        _definitions.Add(definition);
        _linkGroups.Add(0);
        _blocksMovement.Add(definition.BlocksMovement);
        _idsByKey.Add(definition.Key, id);
        return id;
    }

    /// <summary>Whether typeId's definition blocks movement; false for None or an unknown id.</summary>
    public bool BlocksMovement(ushort typeId) => typeId < _blocksMovement.Count && _blocksMovement[typeId];

    /// <summary>typeId's contact, if its definition has one; false for None, an unknown id, or terrain that does nothing to what stands on it.</summary>
    public bool TryGetContact(ushort typeId, [NotNullWhen(true)] out TerrainContact? contact)
    {
        contact = TryGet(typeId, out var definition) ? definition.Contact : null;
        return contact is not null;
    }

    /// <summary>The number of registered definitions, not counting None.</summary>
    public int Count => _definitions.Count - 1;

    public bool TryGet(ushort typeId, out TerrainDefinition definition)
    {
        if (typeId < _definitions.Count && _definitions[typeId] is { } found)
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    public bool TryGetId(string key, out ushort typeId) => _idsByKey.TryGetValue(key, out typeId);

    /// <summary>The id registered under key; throws if nothing is.</summary>
    public ushort GetId(string key) =>
        _idsByKey.TryGetValue(key, out var typeId) ? typeId : throw new KeyNotFoundException($"No terrain registered under '{key}'.");

    /// <summary>
    /// typeId's link group as an int: structures with the same one join each other. Read from the definition (its LinkGroup,
    /// else its SpriteName, else its own key), so a door frame joins a stone wall by sharing its group; a definition with
    /// neither joins only itself. Comparing ints keeps the neighbour test free of string lookups.
    /// </summary>
    public int GetLinkGroup(ushort typeId)
    {
        if (typeId >= _linkGroups.Count || typeId == None)
        {
            return 0;
        }

        if (_linkGroups[typeId] != 0)
        {
            return _linkGroups[typeId];
        }

        var definition = _definitions[typeId]!;
        var name = definition.LinkGroup ?? definition.SpriteName ?? $"terrain:{definition.Key}";
        if (!_linkGroupIdsByName.TryGetValue(name, out var linkGroup))
        {
            linkGroup = _linkGroupIdsByName.Count + 1;
            _linkGroupIdsByName.Add(name, linkGroup);
        }

        return _linkGroups[typeId] = linkGroup;
    }
}
