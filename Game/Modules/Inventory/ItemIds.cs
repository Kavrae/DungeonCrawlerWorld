namespace Game.Modules.Inventory;

/// <summary>The interned table of every item definition id ever seen, addressed by a 2-byte handle so a stack stays small.</summary>
/// <remarks>
/// Same shape and reasoning as EntityIdentities (Game.World): append-only, never pruned, and a handle
/// never changes meaning, so an inventory stack can name its item in 2 bytes instead of the 16 a Guid
/// costs -- at roughly 584,000 stacks in a loaded window, that difference is tens of megabytes.
/// Static because stacks are built where only a ComponentManager is at hand (blueprints, loot grants,
/// shop stock), and an interned id means the same thing in every session. Locked because tests run in
/// parallel.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class ItemIds
{
    /// <summary>The handle of no item.</summary>
    public const ushort None = 0;

    private static readonly Lock Gate = new();
    private static readonly List<Guid> DefinitionIds = [Guid.Empty];
    private static readonly Dictionary<Guid, ushort> HandlesByDefinitionId = new() { [Guid.Empty] = None };

    /// <summary>The handle for definitionId, adding it if it has never been seen.</summary>
    public static ushort IdFor(Guid definitionId)
    {
        lock (Gate)
        {
            if (!HandlesByDefinitionId.TryGetValue(definitionId, out var handle))
            {
                if (DefinitionIds.Count > ushort.MaxValue)
                {
                    throw new InvalidOperationException($"More than {ushort.MaxValue} distinct item definitions have been seen.");
                }

                handle = (ushort)DefinitionIds.Count;
                DefinitionIds.Add(definitionId);
                HandlesByDefinitionId.Add(definitionId, handle);
            }

            return handle;
        }
    }

    /// <summary>The definition id handle refers to, or Guid.Empty for a handle never issued.</summary>
    public static Guid DefinitionIdOf(ushort handle)
    {
        lock (Gate)
        {
            return handle < DefinitionIds.Count ? DefinitionIds[handle] : Guid.Empty;
        }
    }
}
