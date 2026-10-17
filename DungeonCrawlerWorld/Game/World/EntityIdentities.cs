namespace Game.World;

/// <summary>The interned table of every EntityIdentity ever recorded, addressed by a 4-byte handle so a ActionSource stays small and reference-free.</summary>
/// <remarks>
/// Append-only and never pruned: entities that aren't Crawlers share one entry per name, so it grows
/// only with the Crawlers that have actually been recorded, and a handle never changes meaning. A
/// ActionSource has room for 16.7 million handles.
/// Static because sources are created where only a ComponentManager is at hand (blueprints, action
/// effects), and an interned identity means the same thing in every session, so sharing it is
/// harmless. Locked because tests run in parallel.
/// </remarks>
public static class EntityIdentities
{
    /// <summary>The handle of the identity with no name and no crawler number.</summary>
    public const int UnknownHandle = 0;

    private static readonly Lock Gate = new();
    private static readonly List<EntityIdentity> Identities = [new EntityIdentity(null, null)];
    private static readonly Dictionary<EntityIdentity, int> HandlesByIdentity = new() { [new EntityIdentity(null, null)] = UnknownHandle };

    /// <summary>The handle for identity, adding it if it has never been recorded.</summary>
    public static int Intern(EntityIdentity identity)
    {
        lock (Gate)
        {
            if (!HandlesByIdentity.TryGetValue(identity, out var handle))
            {
                handle = Identities.Count;
                Identities.Add(identity);
                HandlesByIdentity.Add(identity, handle);
            }

            return handle;
        }
    }

    /// <summary>The identity handle refers to, or the unknown identity for a handle never issued.</summary>
    public static EntityIdentity Get(int handle)
    {
        lock (Gate)
        {
            return (uint)handle < (uint)Identities.Count ? Identities[handle] : Identities[UnknownHandle];
        }
    }
}
