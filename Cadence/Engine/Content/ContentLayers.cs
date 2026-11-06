using System.Text.Json;
using Engine.Json;

namespace Engine.Content;

/// <summary>A record as the layer stack resolved it: its value, and the layer, file and line it was read from.</summary>
/// <param name="Line">Its line in a JSON Lines table; 1 for a document of its own.</param>
/// <param name="Source">A document's position of every value; null for a table's record.</param>
public sealed record ContentLayerRecord<T>(Guid Id, T Value, ContentLayer Layer, string File, int Line, JsonSourceMap? Source = null)
    where T : class;

/// <summary>One table, or one folder of documents, read across every layer of a stack.</summary>
public sealed class LayeredRecords<T>
    where T : class
{
    private readonly IReadOnlyDictionary<ContentLayer, IReadOnlyDictionary<string, int>> _moduleVersionsByLayer;

    internal LayeredRecords(IReadOnlyList<ContentLayerRecord<T>> records, IReadOnlyDictionary<Guid, Guid> redirects, IReadOnlyDictionary<ContentLayer, IReadOnlyDictionary<string, int>> moduleVersionsByLayer)
    {
        Records = records;
        Redirects = redirects;
        _moduleVersionsByLayer = moduleVersionsByLayer;
    }

    /// <summary>The winning record of every enabled Guid: in the order each Guid first appeared, lowest layer first, so a replacement keeps the place of what it replaces.</summary>
    public IReadOnlyList<ContentLayerRecord<T>> Records { get; }

    /// <summary>Each disabled Guid that names a replacement, from its Guid to the replacement's, as the highest layer naming the Guid left it.</summary>
    public IReadOnlyDictionary<Guid, Guid> Redirects { get; }

    /// <summary>The content version layer's file gives moduleName: the default when its header names none, or the layer has no such file.</summary>
    public int ModuleVersionIn(ContentLayer layer, string moduleName) =>
        _moduleVersionsByLayer.TryGetValue(layer, out var versions)
            ? versions.GetValueOrDefault(moduleName, ContentTypeRegistry.DefaultContentVersion)
            : ContentTypeRegistry.DefaultContentVersion;
}

/// <summary>Every source of content a build reads, in order -- master, then mods in load order, then the save, then an IDE's drafts -- and how a table is read across them.</summary>
/// <remarks>
/// A later layer's line for a Guid wins over an earlier one while it is there; within one layer the last line for a Guid
/// wins. An enabled line puts its record in (an override keeps the place of what it overrides) and clears any redirect
/// from its Guid; a disabled line ("off") takes the Guid's record out and sets its redirect to its replacement, if any;
/// a save's removal line ("rm") takes the save's own override away, so the lower layer's record is back. A save may not
/// disable a record, and only a save may remove one: either is a failure of that line, which is left out. A redirect a
/// lower layer set survives a higher layer's override of its target, which it then resolves to. Every failure carries its
/// layer's name (ContentFailure.Layer); what one costs -- a stopped build, a dropped mod, a skipped record -- is the
/// caller's to decide by the layer's kind.
/// </remarks>
public sealed class ContentLayers
{
    public ContentLayers(IReadOnlyList<ContentLayer> layers)
    {
        for (var index = 1; index < layers.Count; index++)
        {
            if (layers[index].Kind < layers[index - 1].Kind)
            {
                throw new ArgumentException($"Content layers stack master, mods, save, drafts in that order; {layers[index]} comes after {layers[index - 1]}.", nameof(layers));
            }
        }

        var duplicate = layers.GroupBy(layer => layer.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Two content layers are named {duplicate.Key}.", nameof(layers));
        }

        Layers = layers;
    }

    public IReadOnlyList<ContentLayer> Layers { get; }

    /// <summary>These layers with layer on top.</summary>
    public ContentLayers With(ContentLayer layer) => new([.. Layers, layer]);

    /// <summary>The layer named name; false when none is.</summary>
    public bool TryGetLayer(string name, out ContentLayer layer)
    {
        layer = Layers.FirstOrDefault(candidate => candidate.Name == name)!;
        return layer is not null;
    }

    /// <summary>Table tableName, the file fileName in each layer, resolved across the stack; every record that fails is added to failures and left out.</summary>
    /// <remarks>
    /// Resolved as IndexTable and Load resolve it: each Guid's winning line is decoded, and one that fails its codec is left
    /// out for the lower layer's record it replaced -- never for an earlier line of its own layer, which the winning line
    /// superseded -- so a table reads the same whole or by id, and as compacting a save's tables leaves it.
    /// </remarks>
    public LayeredRecords<T> ReadTable<T>(string fileName, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        var index = IndexTable(fileName, tableName, static _ => 0, failures);
        var records = new List<ContentLayerRecord<T>>(index.Count);
        foreach (var entry in index.Entries)
        {
            for (ContentIndexEntry<int>? candidate = entry; candidate is not null; candidate = candidate.Beneath)
            {
                if (Load<T, int>(candidate, types, references, failures) is { } value)
                {
                    records.Add(new ContentLayerRecord<T>(entry.Id, value, candidate.Layer, candidate.File, candidate.Line));
                    break;
                }
            }
        }

        return new LayeredRecords<T>(records, index.Redirects, index.ModuleVersionsByLayer);
    }

    /// <summary>Every document of table tableName in each layer's folder whose name ends in extension, in file name order within a layer, resolved across the stack; every record that fails is added to failures and left out.</summary>
    public LayeredRecords<T> ReadDocuments<T>(string folder, string extension, string tableName, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        var lines = new List<(ContentLayer Layer, IReadOnlyList<LayerLine<T>> Lines)>();
        foreach (var layer in Layers)
        {
            var layerLines = new List<LayerLine<T>>();
            foreach (var path in layer.FilesIn(folder, extension))
            {
                layer.TryReadText(path, out var text);
                var failureCount = failures.Count;
                var record = ContentDocument.Read<T>(text, path, tableName, types, references, failures);
                NameLayer(failures, failureCount, layer);
                if (record is not null)
                {
                    layerLines.Add(new LayerLine<T>(record.Id, record.Value, record.IsDisabled, record.Replacement, IsRemoval: false, path, Line: 1, record.Source));
                }
            }

            lines.Add((layer, layerLines));
        }

        return Merge(lines, new Dictionary<ContentLayer, IReadOnlyDictionary<string, int>>(), failures);
    }

    /// <summary>Table tableName, the file fileName in each layer, indexed across the stack without decoding a record: each winning Guid's layer, file, line and bytes, and summarize's summary of it; every line that fails is added to failures and left out.</summary>
    /// <remarks>The rules ReadTable reads by: a record failing its codec is only found when Load decodes it.</remarks>
    public ContentIndex<TSummary> IndexTable<TSummary>(string fileName, string tableName, Func<JsonElement, TSummary> summarize, List<ContentFailure> failures)
    {
        var lines = new List<(ContentLayer Layer, IReadOnlyList<LayerLine<IndexedLine<TSummary>>> Lines)>();
        var moduleVersions = new Dictionary<ContentLayer, IReadOnlyDictionary<string, int>>();
        foreach (var layer in Layers)
        {
            if (!layer.TryReadBytes(fileName, out var bytes))
            {
                continue;
            }

            var failureCount = failures.Count;
            var file = ContentTable.ReadIndex(bytes, fileName, tableName, summarize, failures);
            NameLayer(failures, failureCount, layer);
            moduleVersions[layer] = file.ModuleVersions;
            lines.Add((layer, [.. file.Lines.Select(line => new LayerLine<IndexedLine<TSummary>>(line.Id, new IndexedLine<TSummary>(line.Offset, line.Length, line.Summary!), line.IsDisabled, line.Replacement, line.IsRemoval, fileName, line.Line, Source: null))]));
        }

        var (winners, redirects) = MergeLines(lines, failures);
        var entries = winners.Select(IndexEntryOf).ToList();
        return new ContentIndex<TSummary>(tableName, entries, redirects, moduleVersions);
    }

    /// <summary>The record entry names, read from its layer and decoded through T's codec; null, with a failure naming its layer added, when it doesn't decode.</summary>
    public static T? Load<T, TSummary>(ContentIndexEntry<TSummary> entry, ContentTypeRegistry types, IContentReferences references, List<ContentFailure> failures)
        where T : class
    {
        var failureCount = failures.Count;
        var value = ContentTable.ReadLine<T>(entry.Layer.ReadBytes(entry.File, entry.Offset, entry.Length), entry.File, entry.Line, types, references, failures);
        NameLayer(failures, failureCount, entry.Layer);
        return value;
    }

    private static ContentIndexEntry<TSummary> IndexEntryOf<TSummary>(MergedLine<IndexedLine<TSummary>> line) =>
        new(line.Id, line.Layer, line.File, line.Line, line.Payload.Offset, line.Payload.Length, line.Payload.Summary, line.Beneath is null ? null : IndexEntryOf(line.Beneath));

    private static void NameLayer(List<ContentFailure> failures, int from, ContentLayer layer)
    {
        for (var index = from; index < failures.Count; index++)
        {
            failures[index] = failures[index] with { Layer = layer.Name };
        }
    }

    private static LayeredRecords<T> Merge<T>(IEnumerable<(ContentLayer Layer, IReadOnlyList<LayerLine<T>> Lines)> lines, IReadOnlyDictionary<ContentLayer, IReadOnlyDictionary<string, int>> moduleVersions, List<ContentFailure> failures)
        where T : class
    {
        var (winners, redirects) = MergeLines(lines, failures);
        var records = winners.Select(winner => new ContentLayerRecord<T>(winner.Id, winner.Payload, winner.Layer, winner.File, winner.Line, winner.Source)).ToList();
        return new LayeredRecords<T>(records, redirects, moduleVersions);
    }

    /// <summary>The winning line of every enabled Guid, each over the lower layer's line it replaced, in first-appearance order, and the redirects left -- the layering rules in the class remarks, over lines carrying any payload.</summary>
    private static (List<MergedLine<TPayload>> Winners, Dictionary<Guid, Guid> Redirects) MergeLines<TPayload>(
        IEnumerable<(ContentLayer Layer, IReadOnlyList<LayerLine<TPayload>> Lines)> lines, List<ContentFailure> failures)
    {
        var order = new List<Guid>();
        var seen = new HashSet<Guid>();
        var current = new Dictionary<Guid, MergedLine<TPayload>>();
        var redirects = new Dictionary<Guid, Guid>();

        foreach (var (layer, layerLines) in lines)
        {
            var winning = new Dictionary<Guid, LayerLine<TPayload>>();
            var layerOrder = new List<Guid>();
            foreach (var line in layerLines)
            {
                if (line.IsRemoval && layer.Kind != ContentLayerKind.Save)
                {
                    failures.Add(new ContentFailure(line.File, line.Line, line.Id, $"Only a save removes an override (\"{ContentTable.RemovalKey}\").", Layer: layer.Name));
                    continue;
                }

                if (line.IsDisabled && layer.Kind == ContentLayerKind.Save)
                {
                    failures.Add(new ContentFailure(line.File, line.Line, line.Id, $"A save can't disable a record (\"{ContentTable.DisabledKey}\"); only master and mods do.", Layer: layer.Name));
                    continue;
                }

                if (winning.TryAdd(line.Id, line))
                {
                    layerOrder.Add(line.Id);
                }
                else
                {
                    winning[line.Id] = line;
                }
            }

            foreach (var id in layerOrder)
            {
                var line = winning[id];
                if (line.IsRemoval)
                {
                    continue;
                }

                redirects.Remove(id);
                if (line.IsDisabled)
                {
                    current.Remove(id);
                    if (line.Replacement is { } replacement)
                    {
                        redirects[id] = replacement;
                    }

                    continue;
                }

                current[id] = new MergedLine<TPayload>(id, line.Payload!, layer, line.File, line.Line, line.Source, current.GetValueOrDefault(id));
                if (seen.Add(id))
                {
                    order.Add(id);
                }
            }
        }

        var winners = new List<MergedLine<TPayload>>(current.Count);
        foreach (var id in order)
        {
            if (current.TryGetValue(id, out var winner))
            {
                winners.Add(winner);
            }
        }

        return (winners, redirects);
    }

    private sealed record MergedLine<TPayload>(Guid Id, TPayload Payload, ContentLayer Layer, string File, int Line, JsonSourceMap? Source, MergedLine<TPayload>? Beneath);

    private sealed record LayerLine<TPayload>(Guid Id, TPayload? Payload, bool IsDisabled, Guid? Replacement, bool IsRemoval, string File, int Line, JsonSourceMap? Source);

    private sealed record IndexedLine<TSummary>(long Offset, int Length, TSummary Summary);
}
