namespace Game.Modules.Auras;

/// <summary>Every aura definition the session has met, by session-local id and by Guid.</summary>
/// <remarks>
/// <para>
/// No module registers an aura. A definition is content (the Auras table, across master, mods and the save) and is
/// registered as the session meets it: the build registers every aura its terrain, blueprints and content name
/// (AuraContentRegistration), a game loading by id registers an action's or item's auras as it loads them
/// (ContentCatalogLoader), and a definition created during the session (ContentStore.CreatePersistent) registers on
/// the way in. Register is how a definition becomes an id, and registering a Guid again is free.
/// </para>
/// <para>
/// A Guid's definition is fixed for the session by the first registration of it, which is the content's winning
/// record -- the newest valid layer's -- so every source of that aura, whatever holds it, radiates the same effects and
/// colour. Nothing replaces it during a session: a save's override of it takes effect when the next session loads.
/// Whatever needs an aura's effects or colour reads it from here by id.
/// </para>
/// <para>
/// Ids are assigned in registration order and differ between sessions, so nothing saves or orders by one; a Guid is
/// the identity that lasts, and what makes two sources the same aura: their powers add. Safe to call from a worker
/// planning a neighborhood: registration is locked, and Get reads an array that is replaced rather than changed.
/// </para>
/// </remarks>
public sealed class AuraCatalog
{
    /// <summary>The most aura definitions a session can hold: ids are bytes.</summary>
    public const int Capacity = byte.MaxValue + 1;

    private readonly object _registrationLock = new();
    private readonly Dictionary<Guid, byte> _auraIdsByGuid = [];
    private AuraDefinition[] _definitions = [];

    public int Count => _definitions.Length;

    /// <summary>The session-local id of definition's Guid, registering definition if this is the first time the Guid is seen.</summary>
    /// <remarks>A Guid already registered returns its id and changes nothing, whatever definition is passed: the first registration is what the Guid means for the session.</remarks>
    public byte Register(AuraDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_registrationLock)
        {
            if (_auraIdsByGuid.TryGetValue(definition.Id, out var auraId))
            {
                return auraId;
            }

            if (_definitions.Length >= Capacity)
            {
                throw new InvalidOperationException($"More than {Capacity} aura definitions registered.");
            }

            auraId = (byte)_definitions.Length;
            _definitions = [.. _definitions, definition];
            _auraIdsByGuid.Add(definition.Id, auraId);
            return auraId;
        }
    }

    public AuraDefinition Get(byte auraId) => _definitions[auraId];

    public bool TryGetId(Guid auraGuid, out byte auraId)
    {
        lock (_registrationLock)
        {
            return _auraIdsByGuid.TryGetValue(auraGuid, out auraId);
        }
    }

    /// <summary>The id registered under auraGuid; throws if nothing is.</summary>
    public byte GetId(Guid auraGuid) =>
        TryGetId(auraGuid, out var auraId) ? auraId : throw new KeyNotFoundException($"No aura registered under '{auraGuid}'.");
}
