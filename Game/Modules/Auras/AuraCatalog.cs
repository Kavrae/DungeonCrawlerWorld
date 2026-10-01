namespace Game.Modules.Auras;

/// <summary>Every aura definition the session has met, by session-local id and by Guid.</summary>
/// <remarks>
/// <para>
/// No module registers an aura. A definition lives with whatever radiates it -- a terrain, a
/// blueprint, an effect entry -- and is registered when that content is: the build registers every
/// definition its content names (AuraContentRegistration), and anything that names one later
/// (terrain registered during a session, a definition made at runtime) registers it on the way in.
/// Register is how a definition becomes an id, and registering the same definition again is free.
/// </para>
/// <para>
/// Ids are assigned in registration order and differ between sessions, so nothing saves or orders
/// by one; a Guid is the identity that lasts, and what makes two sources the same aura: their
/// strengths add. Nothing keeps a copy of a definition: whatever needs an aura's effects or colour
/// reads it from here by id, so a definition replaced during a session is what is used next.
/// </para>
/// <para>
/// Safe to call from a worker planning a neighborhood: registration is locked, and Get reads an
/// array that is replaced rather than changed.
/// </para>
/// </remarks>
public sealed class AuraCatalog
{
    private readonly object _registrationLock = new();
    private readonly Dictionary<Guid, byte> _auraIdsByGuid = [];
    private AuraDefinition[] _definitions = [];

    public int Count => _definitions.Length;

    /// <summary>A registered Guid was registered again with a different definition: the id whose definition was replaced.</summary>
    /// <remarks>For what follows from a definition and is held outside it: the glow already drawn, and who holds an exposure to the aura.</remarks>
    public event Action<byte>? DefinitionChanged;

    /// <summary>The session-local id of definition, registering it if this is the first time its Guid is seen.</summary>
    /// <remarks>
    /// A Guid already registered with this same definition returns its id and changes nothing. With
    /// a different definition it replaces the old one in place, keeps the id and raises
    /// DefinitionChanged -- how a mod's content overrides a built-in aura, and how an aura changes
    /// during a session.
    /// </remarks>
    public byte Register(AuraDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        byte auraId;
        lock (_registrationLock)
        {
            if (_auraIdsByGuid.TryGetValue(definition.Id, out auraId))
            {
                var registered = _definitions[auraId];
                if (ReferenceEquals(registered, definition) || registered.Equals(definition))
                {
                    return auraId;
                }

                var replaced = (AuraDefinition[])_definitions.Clone();
                replaced[auraId] = definition;
                _definitions = replaced;
            }
            else
            {
                if (_definitions.Length > byte.MaxValue)
                {
                    throw new InvalidOperationException($"More than {byte.MaxValue + 1} aura definitions registered.");
                }

                auraId = (byte)_definitions.Length;
                _definitions = [.. _definitions, definition];
                _auraIdsByGuid.Add(definition.Id, auraId);
                return auraId;
            }
        }

        DefinitionChanged?.Invoke(auraId);
        return auraId;
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
