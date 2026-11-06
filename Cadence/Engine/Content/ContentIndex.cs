namespace Engine.Content;

/// <summary>One record of a table as ContentLayers.IndexTable resolved it: its Guid, the layer, file and line it is read from, where its bytes are, and its summary.</summary>
/// <param name="Offset">Where the line's bytes start in File.</param>
/// <param name="Length">How many bytes the line is.</param>
/// <param name="Beneath">The lower layer's record of the same Guid this one replaced; null when no lower layer holds it enabled.</param>
public sealed record ContentIndexEntry<TSummary>(Guid Id, ContentLayer Layer, string File, int Line, long Offset, int Length, TSummary Summary, ContentIndexEntry<TSummary>? Beneath = null);

/// <summary>Every record of one table across a stack of layers, found without decoding any: the winning line of each enabled Guid, the redirects, and each layer's module versions.</summary>
/// <remarks>Read by whoever loads records one at a time (ContentLayers.Load), and asked whole-catalog questions through each entry's summary instead of loading every record.</remarks>
public sealed class ContentIndex<TSummary>
{
    private readonly Dictionary<Guid, ContentIndexEntry<TSummary>> _entriesById;
    private readonly IReadOnlyDictionary<ContentLayer, IReadOnlyDictionary<string, int>> _moduleVersionsByLayer;

    internal ContentIndex(string tableName, IReadOnlyList<ContentIndexEntry<TSummary>> entries, IReadOnlyDictionary<Guid, Guid> redirects, IReadOnlyDictionary<ContentLayer, IReadOnlyDictionary<string, int>> moduleVersionsByLayer)
    {
        TableName = tableName;
        Entries = entries;
        Redirects = redirects;
        _moduleVersionsByLayer = moduleVersionsByLayer;
        _entriesById = entries.ToDictionary(entry => entry.Id);
    }

    public string TableName { get; }

    /// <summary>The winning record of every enabled Guid, in the order each Guid first appeared, lowest layer first.</summary>
    public IReadOnlyList<ContentIndexEntry<TSummary>> Entries { get; }

    /// <summary>Each disabled Guid that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> Redirects { get; }

    public int Count => Entries.Count;

    internal IReadOnlyDictionary<ContentLayer, IReadOnlyDictionary<string, int>> ModuleVersionsByLayer => _moduleVersionsByLayer;

    public bool Contains(Guid id) => _entriesById.ContainsKey(id);

    /// <summary>The entry of id itself; false for a Guid no layer holds enabled (a redirect isn't followed here).</summary>
    public bool TryGet(Guid id, out ContentIndexEntry<TSummary> entry) => _entriesById.TryGetValue(id, out entry!);

    /// <summary>The entry id resolves to: its own, else through redirects (a cycle resolves to nothing).</summary>
    public bool TryResolve(Guid id, out ContentIndexEntry<TSummary> entry)
    {
        var current = id;
        for (var hops = 0; hops <= Redirects.Count; hops++)
        {
            if (_entriesById.TryGetValue(current, out entry!))
            {
                return true;
            }

            if (!Redirects.TryGetValue(current, out current))
            {
                break;
            }
        }

        entry = null!;
        return false;
    }

    /// <summary>The content version layer's file gives moduleName: the default when its header names none, or the layer has no such file.</summary>
    public int ModuleVersionIn(ContentLayer layer, string moduleName) =>
        _moduleVersionsByLayer.TryGetValue(layer, out var versions)
            ? versions.GetValueOrDefault(moduleName, ContentTypeRegistry.DefaultContentVersion)
            : ContentTypeRegistry.DefaultContentVersion;
}
