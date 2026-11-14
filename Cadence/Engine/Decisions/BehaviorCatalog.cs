using Engine.Modules;
using Engine.Tags;

namespace Engine.Decisions;

/// <summary>The behaviors a build's modules registered, by Guid, each with a session-local id once frozen.</summary>
/// <remarks>
/// Modules register in Configure; re-registering a Guid replaces the behavior in place (how a mod replaces a built-in),
/// and a redirect makes an old Guid resolve to another, as in any Catalog. Freeze ends registration for the session --
/// behaviors, like every definition, are fixed once the build resolves its sets -- checks every behavior's tags against
/// the domain behaviors are tagged in, and numbers them 1 up in registration order. A session id is what a commitment
/// stores; it is never persisted, since another session may number them differently.
/// </remarks>
public sealed class BehaviorCatalog<TContext>
    where TContext : IDecisionContext, allows ref struct
{
    private readonly Catalog<IBehavior<TContext>> _behaviors = new(static behavior => behavior.Id);
    private readonly Dictionary<Guid, ushort> _sessionIdsById = [];
    private IBehavior<TContext>[] _behaviorsBySessionId = [];

    public int Count => _behaviors.Count;

    public bool IsFrozen { get; private set; }

    /// <summary>The domain every behavior's tags and every modifier's tag must be in; set by Freeze.</summary>
    public GameplayTagDomain? TagDomain { get; private set; }

    /// <summary>The build's tags, which behaviors' and modifiers' tags are checked against; set by Freeze.</summary>
    public GameplayTagRegistry? Tags { get; private set; }

    /// <summary>Every registered behavior, in registration order.</summary>
    public IEnumerable<IBehavior<TContext>> Behaviors => _behaviors.Definitions;

    /// <exception cref="InvalidOperationException">The catalog is frozen.</exception>
    public void Register(IBehavior<TContext> behavior)
    {
        EnsureNotFrozen($"register '{behavior.Name}'");
        _behaviors.Register(behavior);
    }

    /// <summary>Makes from resolve to whatever to resolves to, whenever no behavior is registered under from.</summary>
    /// <exception cref="InvalidOperationException">The catalog is frozen.</exception>
    public void AddRedirect(Guid from, Guid to)
    {
        EnsureNotFrozen($"redirect {from}");
        _behaviors.AddRedirect(from, to);
    }

    public bool TryGet(Guid id, out IBehavior<TContext> behavior) => _behaviors.TryGet(id, out behavior);

    /// <exception cref="InvalidOperationException">No behavior is registered under id, or redirected to from it.</exception>
    public IBehavior<TContext> Get(Guid id) => _behaviors.Get(id);

    /// <summary>Ends registration, checks every behavior's tags against tagDomain, and gives each behavior its session id.</summary>
    /// <exception cref="InvalidOperationException">Already frozen, or a behavior's tag is undeclared or outside tagDomain.</exception>
    public void Freeze(GameplayTagRegistry tags, GameplayTagDomain tagDomain)
    {
        EnsureNotFrozen("freeze it again");
        foreach (var behavior in _behaviors.Definitions)
        {
            tags.EnsureDeclared(behavior.Tags, $"Behavior '{behavior.Name}'");
            tags.EnsureAccepts(tagDomain, behavior.Tags, $"Behavior '{behavior.Name}'");
        }

        if (_behaviors.Count >= ushort.MaxValue)
        {
            throw new InvalidOperationException($"A session holds at most {ushort.MaxValue - 1} behaviors; {_behaviors.Count} are registered.");
        }

        _behaviorsBySessionId = [null!, .. _behaviors.Definitions];
        for (var sessionId = 1; sessionId < _behaviorsBySessionId.Length; sessionId++)
        {
            _sessionIdsById[_behaviorsBySessionId[sessionId].Id] = (ushort)sessionId;
        }

        Tags = tags;
        TagDomain = tagDomain;
        IsFrozen = true;
    }

    /// <summary>The session id of the behavior registered under behaviorId.</summary>
    /// <exception cref="InvalidOperationException">The catalog isn't frozen, or no behavior is registered under behaviorId.</exception>
    public ushort SessionIdOf(Guid behaviorId)
    {
        if (!IsFrozen)
        {
            throw new InvalidOperationException("Session ids are issued when the behavior catalog is frozen.");
        }

        return _sessionIdsById.TryGetValue(behaviorId, out var sessionId)
            ? sessionId
            : throw new InvalidOperationException($"No behavior is registered as {behaviorId}.");
    }

    /// <summary>The behavior sessionId names, or null for 0 or an id this session never issued.</summary>
    public IBehavior<TContext>? BySessionId(ushort sessionId) =>
        sessionId > 0 && sessionId < _behaviorsBySessionId.Length ? _behaviorsBySessionId[sessionId] : null;

    private void EnsureNotFrozen(string attempt)
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException($"Can't {attempt}: the behavior catalog is frozen for the session.");
        }
    }
}
