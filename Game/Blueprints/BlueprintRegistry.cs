using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Lootboxes;
using Game.Spawning;

namespace Game.Blueprints;

/// <summary>Every blueprint definition an entity can be spawned from, each under a session-local ushort id, and each resolved once into what it builds.</summary>
/// <remarks>
/// Ids follow registration order and are only stable within a session, so anything persisted stores
/// the Guid. Id 0 is <see cref="None"/>. Registering a Guid that is already registered replaces its
/// definition under the same id, the way a mod replaces a built-in, and drops every resolution, since
/// any composite may include it. Resolution is lazy and cached; ResolveAll runs it for everything
/// once configuration is done, so a broken include fails at startup rather than at its first spawn.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class BlueprintRegistry
{
    public const ushort None = 0;

    /// <summary>The deepest a chain of includes may nest, counting the definition being resolved.</summary>
    public const int MaximumIncludeDepth = 8;

    /// <summary>What every composed Guid is derived under, so none can collide with a hand-written one by accident.</summary>
    private static readonly Guid ComposedIdNamespace = new("a3c1f0e2-6b7d-4e59-8f10-2c4d5e6f7a81");

    private readonly List<BlueprintDefinition?> _definitions = [null];
    private readonly Dictionary<Guid, ushort> _idsByGuid = [];
    private ResolvedBlueprint?[] _resolved = [null];

    public BlueprintRegistry()
    {
        Races = new BlueprintKindView(this, "race", static definition => definition.Race is not null);
        Classes = new BlueprintKindView(this, "class", static definition => definition.Class is not null);
    }

    /// <summary>The definitions that are races -- looked up by the ids RaceSlotsComponent holds.</summary>
    public BlueprintKindView Races { get; }

    /// <summary>The definitions that are classes -- looked up by the ids ClassSlotsComponent holds.</summary>
    public BlueprintKindView Classes { get; }

    /// <summary>How many definitions are registered; their ids run from 1 to this.</summary>
    public int Count => _definitions.Count - 1;

    public ushort Register(BlueprintDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (_idsByGuid.TryGetValue(definition.Id, out var existingId))
        {
            _definitions[existingId] = definition;
            Array.Clear(_resolved);
            return existingId;
        }

        if (_definitions.Count > ushort.MaxValue)
        {
            throw new InvalidOperationException($"More than {ushort.MaxValue} blueprint definitions registered.");
        }

        _definitions.Add(definition);
        var id = (ushort)(_definitions.Count - 1);
        _idsByGuid.Add(definition.Id, id);
        return id;
    }

    public BlueprintDefinition Get(ushort id) =>
        TryGet(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"No blueprint registered under id {id}.");

    public bool TryGet(ushort id, out BlueprintDefinition definition)
    {
        if (id < _definitions.Count && _definitions[id] is { } found)
        {
            definition = found;
            return true;
        }

        definition = null!;
        return false;
    }

    public ushort GetId(Guid definitionId) =>
        _idsByGuid.TryGetValue(definitionId, out var id)
            ? id
            : throw new KeyNotFoundException($"No blueprint registered as {definitionId}.");

    public bool TryGetId(Guid definitionId, out ushort id) => _idsByGuid.TryGetValue(definitionId, out id);

    /// <summary>What the definition under id builds.</summary>
    /// <exception cref="InvalidOperationException">Its includes name an unregistered Guid, include themselves, or nest deeper than <see cref="MaximumIncludeDepth"/>.</exception>
    public ResolvedBlueprint Resolve(ushort id) =>
        TryResolve(id, out var resolved)
            ? resolved
            : throw new KeyNotFoundException($"No blueprint registered under id {id}.");

    /// <inheritdoc cref="Resolve"/>
    /// <returns>False when nothing is registered under id.</returns>
    public bool TryResolve(ushort id, out ResolvedBlueprint resolved)
    {
        if (id < _resolved.Length && _resolved[id] is { } cached)
        {
            resolved = cached;
            return true;
        }

        if (!TryGet(id, out _))
        {
            resolved = null!;
            return false;
        }

        if (id >= _resolved.Length)
        {
            Array.Resize(ref _resolved, _definitions.Count);
        }

        resolved = _resolved[id] = ResolveUncached(id);
        return true;
    }

    /// <summary>The id of a blueprint made of exactly includes, in that order, registering it the first time it is asked for.</summary>
    /// <remarks>
    /// For a combination that doesn't justify a named definition of its own -- a runtime or data-driven
    /// one. Its Guid is derived from the include Guids alone, so the same combination is the same
    /// blueprint in every session, and a save that records the includes can re-intern it. Its name joins
    /// the includes' names; its appearance, actions and the rest resolve like any composite's.
    /// </remarks>
    public ushort Compose(params Guid[] includes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(includes.Length);

        var id = ComposedId(includes);
        if (TryGetId(id, out var existing))
        {
            return existing;
        }

        var name = string.Join(" + ", includes.Select(include => Get(GetId(include)).Name));
        return Register(new BlueprintDefinition(id, name) { Includes = includes });
    }

    /// <summary>Resolves every registered definition, so a broken one fails here rather than when it is first spawned.</summary>
    public void ResolveAll()
    {
        for (var id = (ushort)1; id < _definitions.Count; id++)
        {
            Resolve(id);
        }
    }

    /// <summary>How blueprintId's entities look and are called -- what an entity with no visual or name of its own draws and names itself as.</summary>
    public bool TryGetAppearance(ushort blueprintId, out EntityAppearance appearance)
    {
        if (TryResolve(blueprintId, out var resolved))
        {
            appearance = resolved.Appearance;
            return true;
        }

        appearance = null!;
        return false;
    }

    /// <inheritdoc cref="ResolvedBlueprint.NameFor"/>
    public string NameFor(SpawnRecordComponent record) =>
        TryResolve(record.BlueprintId, out var resolved) ? resolved.NameFor(record.Seed) : string.Empty;

    private ResolvedBlueprint ResolveUncached(ushort id)
    {
        var buildOrder = new List<ushort>();
        Flatten(id, buildOrder, []);

        var races = new List<ushort>();
        var classes = new List<ushort>();
        var actions = new List<ActionGrant>();
        var nonBlocking = new List<NonBlockingKind>();
        var appearance = new AppearanceBuilder();
        MapLayer? layer = null;
        Vector2Byte? size = null;
        LootboxReward? lootbox = null;

        foreach (var partId in buildOrder)
        {
            var part = Get(partId);
            lootbox = part.Lootbox ?? lootbox;

            if (part.Race is null || races.Count == 0)
            {
                if (part.Appearance is { } facet)
                {
                    appearance.Apply(facet);
                }

                layer = part.Layer ?? layer;
                size = part.Size ?? size;
            }

            if (part.Race is not null)
            {
                races.Add(partId);
            }

            if (part.Class is not null)
            {
                classes.Add(partId);
                appearance.AddNameSuffix(part.Name);
            }

            foreach (var grant in part.Actions)
            {
                var existing = actions.FindIndex(held => held.ActionId == grant.ActionId);
                if (existing >= 0)
                {
                    actions[existing] = grant;
                }
                else
                {
                    actions.Add(grant);
                }
            }

            if (part.NonBlocking is { } kind)
            {
                nonBlocking.Add(kind);
            }
        }

        return new ResolvedBlueprint(Get(id), [.. buildOrder], [.. races], [.. classes], [.. actions], [.. nonBlocking], appearance.Build(), layer ?? MapLayer.Ground, size ?? new Vector2Byte(1, 1), lootbox);
    }

    /// <summary>Appends id's includes depth-first, then id itself, skipping any already appended.</summary>
    private void Flatten(ushort id, List<ushort> buildOrder, List<ushort> path)
    {
        var definition = Get(id);

        if (path.Contains(id))
        {
            throw new InvalidOperationException($"Blueprint '{definition.Name}' includes itself: {DescribePath(path, id)}.");
        }

        if (path.Count >= MaximumIncludeDepth)
        {
            throw new InvalidOperationException($"Blueprint includes nest deeper than {MaximumIncludeDepth}: {DescribePath(path, id)}.");
        }

        path.Add(id);
        foreach (var include in definition.Includes)
        {
            if (!TryGetId(include, out var includeId))
            {
                throw new InvalidOperationException($"Blueprint '{definition.Name}' includes {include}, which is not registered.");
            }

            Flatten(includeId, buildOrder, path);
        }
        path.RemoveAt(path.Count - 1);

        if (!buildOrder.Contains(id))
        {
            buildOrder.Add(id);
        }
    }

    /// <summary>A Guid that is a pure function of includes: the first 16 bytes of a SHA-256 over them, stamped as an RFC 9562 version 8 (custom) Guid.</summary>
    private static Guid ComposedId(Guid[] includes)
    {
        Span<byte> bytes = stackalloc byte[16 * (includes.Length + 1)];
        ComposedIdNamespace.TryWriteBytes(bytes, bigEndian: true, out _);
        for (var index = 0; index < includes.Length; index++)
        {
            includes[index].TryWriteBytes(bytes[(16 * (index + 1))..], bigEndian: true, out _);
        }

        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(bytes, hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }

    private string DescribePath(List<ushort> path, ushort next) =>
        string.Join(" -> ", path.Append(next).Select(partId => Get(partId).Name));
}

/// <summary>The definitions of one kind -- races or classes -- over the registry's shared id space.</summary>
/// <remarks>An id of another kind reads as "not registered here", with no separate table to keep in step.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class BlueprintKindView
{
    private readonly BlueprintRegistry _registry;
    private readonly string _kind;
    private readonly Func<BlueprintDefinition, bool> _isKind;

    internal BlueprintKindView(BlueprintRegistry registry, string kind, Func<BlueprintDefinition, bool> isKind)
    {
        _registry = registry;
        _kind = kind;
        _isKind = isKind;
    }

    public BlueprintDefinition Get(ushort id) =>
        TryGet(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"No {_kind} registered under id {id}.");

    public bool TryGet(ushort id, out BlueprintDefinition definition) =>
        _registry.TryGet(id, out definition) && _isKind(definition);

    public ushort GetId(Guid definitionId) =>
        TryGetId(definitionId, out var id)
            ? id
            : throw new KeyNotFoundException($"No {_kind} registered as {definitionId}.");

    public bool TryGetId(Guid definitionId, out ushort id) =>
        _registry.TryGetId(definitionId, out id) && _isKind(_registry.Get(id));
}
