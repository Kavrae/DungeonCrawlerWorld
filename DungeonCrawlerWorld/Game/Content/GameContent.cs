using System.Text;
using System.Text.Json;
using Engine.Content;
using Engine.Tags;
using Game.Areas;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Inventory;

namespace Game.Content;

/// <summary>The game's content as a build reads it: the actions, items, auras, area palettes and area placements, one JSON Lines table each, and areas, one file each, resolved across the build's content layers (master, then mods, then a save).</summary>
/// <remarks>
/// Actions, items and auras are indexed, not read whole: opening the content starts a scan of their tables on a worker
/// (ContentLayers.IndexTable), which finds every record's layer, file, line and bytes and a summary of it
/// (DefinitionSummary) without decoding any. A record is decoded the first time it is asked for (TryLoadAction,
/// TryLoadItem, TryLoadAura, or Prefetch from any thread), its auras with it, and the same instance is returned every
/// time after; Actions, Items and Auras decode every record, for a build that wants them all. Whole-catalog questions
/// are answered from the summaries, never from what happens to have been decoded.
///
/// Areas are in the Areas folder, one ContentDocument per file read in file name order, so an area's rows are one per
/// line; they and the area tables are read whole the first time they are asked for. Every record keeps where it was
/// read from (TryGetLocation, and LocatedRecord for areas, palettes and placements), its layer included.
///
/// A disabled record isn't loaded; when it names a replacement, its Guid redirects to that record (AuraRedirects,
/// ActionRedirects, ItemRedirects). A record that fails is a ContentFailure carrying its layer. Content opened to throw
/// (the game's) stops when the failure is in a master or mod layer, which must load whole; a save's failing record is
/// left out and kept in Failures, never stopping a game. Content opened for a tool collects every failure. The game reads
/// the master copy under its output directory (Content.csproj copies Definitions/Master only, never Definitions/Drafts).
/// </remarks>
public sealed class GameContent
{
    public const string ActionsTable = "Actions";
    public const string ItemsTable = "Items";
    public const string AurasTable = "Auras";

    public const string AreasTable = "Areas";
    public const string AreaPalettesTable = "AreaPalettes";
    public const string AreaPlacementsTable = "AreaPlacements";
    public const string RedirectsTable = "Redirects";

    public const string ActionsFileName = "Actions.jsonl";
    public const string ItemsFileName = "Items.jsonl";
    public const string AurasFileName = "Auras.jsonl";
    public const string AreaPalettesFileName = "AreaPalettes.jsonl";
    public const string AreaPlacementsFileName = "AreaPlacements.jsonl";
    public const string RedirectsFileName = "Redirects.jsonl";

    /// <summary>The folder of area files, one area each (an AreasTable document).</summary>
    public const string AreasDirectoryName = "Areas";

    /// <summary>An area file's extension.</summary>
    public const string AreaFileExtension = ".json";

    /// <summary>What the shipped content's layer is called.</summary>
    public const string MasterLayerName = "Master";

    private const string NameKey = "n";
    private const string SpriteNameKey = "sp";
    private const string TagsKey = "t";
    private const string CanTradeKey = "tr";
    private const string ContentsKey = "ct";

    private readonly ContentLayers _layers;
    private readonly ContentTypeRegistry _types;
    private readonly bool _throwsOnFailure;
    private readonly List<ContentFailure> _failures = [];
    private readonly Dictionary<string, Dictionary<Guid, ContentRecordLocation>> _locationsByTable = [];
    private readonly Lock _loadLock = new();
    private readonly Task<Indexes> _indexing;
    private readonly LoadedTable<AuraDefinition> _loadedAuras = new();
    private readonly LoadedTable<ActionDefinition> _loadedActions = new();
    private readonly LoadedTable<ItemDefinition> _loadedItems = new();
    private readonly AuraReferences _auraReferences;
    private bool _hasStoppingFailure;

    private LayeredRecords<AreaDefinition>? _areas;
    private LayeredRecords<AreaPalette>? _areaPalettes;
    private LayeredRecords<AreaPlacement>? _areaPlacements;
    private LayeredRecords<ContentRedirect>? _redirects;

    private GameContent(ContentLayers layers, ContentTypeRegistry types, bool throwsOnFailure)
    {
        _layers = layers;
        _types = types;
        _throwsOnFailure = throwsOnFailure;
        _auraReferences = new AuraReferences(this);
        _indexing = Task.Run(ScanIndexes);
    }

    /// <summary>Where the game's own build output keeps the master manifest.</summary>
    public static string DefaultMasterDirectory => Path.Combine(AppContext.BaseDirectory, "Definitions", "Master");

    /// <summary>The master manifest under directory, as a layer.</summary>
    public static ContentLayer MasterLayer(string directory) => ContentLayer.FromDirectory(MasterLayerName, ContentLayerKind.Master, directory);

    /// <summary>A stack of the master manifest under directory alone.</summary>
    public static ContentLayers MasterOnly(string directory) => new([MasterLayer(directory)]);

    /// <summary>Every table, in the order the game reads them.</summary>
    public static IReadOnlyList<string> TableNames { get; } = [AurasTable, ActionsTable, ItemsTable, AreaPalettesTable, AreaPlacementsTable, AreasTable, RedirectsTable];

    /// <summary>The layers this content is read across, lowest first.</summary>
    public ContentLayers Layers => _layers;

    /// <summary>Every enabled aura, decoded now if it wasn't already, in index order.</summary>
    public IReadOnlyList<AuraDefinition> Auras => LoadAll<AuraDefinition>(IndexesNow.Auras, TryLoadAura);

    /// <summary>Every enabled action, decoded now if it wasn't already, in index order (each Guid where it first appeared) -- the order the catalogs register them in.</summary>
    public IReadOnlyList<ActionDefinition> Actions => LoadAll<ActionDefinition>(IndexesNow.Actions, TryLoadAction);

    /// <summary>Every enabled item, decoded now if it wasn't already, in index order.</summary>
    public IReadOnlyList<ItemDefinition> Items => LoadAll<ItemDefinition>(IndexesNow.Items, TryLoadItem);

    /// <summary>Every enabled action's summary, in Guid order, without decoding any.</summary>
    public IReadOnlyList<DefinitionSummary> ActionSummaries => IndexesNow.ActionSummaries;

    /// <summary>Every enabled item's summary, in Guid order, without decoding any.</summary>
    public IReadOnlyList<DefinitionSummary> ItemSummaries => IndexesNow.ItemSummaries;

    /// <summary>Every enabled area, one per file of the Areas folder (in file name order within a layer), with where it was read from.</summary>
    public IReadOnlyList<LocatedRecord<AreaDefinition>> Areas => Located(_areas ??= ReadAreas());

    /// <summary>Every enabled area palette, in order, with where it was read from.</summary>
    public IReadOnlyList<LocatedRecord<AreaPalette>> AreaPalettes => Located(_areaPalettes ??= ReadTable<AreaPalette>(AreaPalettesFileName, AreaPalettesTable));

    /// <summary>Every enabled fixed area placement, in order, with where it was read from.</summary>
    public IReadOnlyList<LocatedRecord<AreaPlacement>> AreaPlacements => Located(_areaPlacements ??= ReadTable<AreaPlacement>(AreaPlacementsFileName, AreaPlacementsTable));

    /// <summary>Every record removed outright and what it resolves to now (the Redirects table), across layers -- kept, not applied: nothing holds an old Guid until entity saves exist.</summary>
    public IReadOnlyList<ContentRedirect> Redirects => [.. (_redirects ??= ReadTable<ContentRedirect>(RedirectsFileName, RedirectsTable)).Records.Select(record => record.Value)];

    /// <summary>Each disabled area that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> AreaRedirects => (_areas ??= ReadAreas()).Redirects;

    /// <summary>Each disabled area palette that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> AreaPaletteRedirects => (_areaPalettes ??= ReadTable<AreaPalette>(AreaPalettesFileName, AreaPalettesTable)).Redirects;

    /// <summary>Each disabled area placement that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> AreaPlacementRedirects => (_areaPlacements ??= ReadTable<AreaPlacement>(AreaPlacementsFileName, AreaPlacementsTable)).Redirects;

    /// <summary>Each disabled aura that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> AuraRedirects => IndexesNow.Auras.Redirects;

    /// <summary>Each disabled action that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> ActionRedirects => IndexesNow.Actions.Redirects;

    /// <summary>Each disabled item that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> ItemRedirects => IndexesNow.Items.Redirects;

    /// <summary>Every record that couldn't be indexed or loaded so far, each with its layer.</summary>
    public IReadOnlyList<ContentFailure> Failures
    {
        get
        {
            lock (_failures)
            {
                return [.. _failures];
            }
        }
    }

    /// <summary>How many actions, items and auras have been decoded so far, each table counted apart.</summary>
    public (int Actions, int Items, int Auras) LoadedCounts => (_loadedActions.Count, _loadedItems.Count, _loadedAuras.Count);

    private Indexes IndexesNow
    {
        get
        {
            var indexes = _indexing.GetAwaiter().GetResult();
            ThrowIfStopping();
            return indexes;
        }
    }

    /// <summary>The content across layers, each record read when first asked for; a master or mod record that fails throws then, and a save's that fails is left out.</summary>
    public static GameContent Open(ContentLayers layers, ContentTypeRegistry types) => new(layers, types, throwsOnFailure: true);

    /// <summary>The content across layers, each record read when first asked for, a record that fails left out and kept in Failures -- for a tool's build, which shows what is wrong instead of stopping.</summary>
    public static GameContent OpenCollectingFailures(ContentLayers layers, ContentTypeRegistry types) => new(layers, types, throwsOnFailure: false);

    /// <summary>Reads every table across layers now, collecting every failure in Failures rather than throwing; a table a layer lacks is empty in it.</summary>
    public static GameContent Load(ContentLayers layers, ContentTypeRegistry types)
    {
        var content = new GameContent(layers, types, throwsOnFailure: false);
        _ = content.Auras;
        _ = content.Actions;
        _ = content.Items;
        _ = content.AreaPalettes;
        _ = content.AreaPlacements;
        _ = content.Areas;
        _ = content.Redirects;
        return content;
    }

    /// <summary>The action id names -- itself, or what its redirects lead to -- decoded the first time and the same instance after; a record that fails to decode is left out for the lower layer's it replaced; false when no layer holds it enabled or none of those decodes.</summary>
    public bool TryLoadAction(Guid id, out ActionDefinition action) => TryLoad(IndexesNow.Actions, _loadedActions, _auraReferences, id, out action);

    /// <summary>The item id names, as TryLoadAction.</summary>
    public bool TryLoadItem(Guid id, out ItemDefinition item) => TryLoad(IndexesNow.Items, _loadedItems, _auraReferences, id, out item);

    /// <summary>The aura id names, as TryLoadAction.</summary>
    public bool TryLoadAura(Guid id, out AuraDefinition aura) => TryLoad(IndexesNow.Auras, _loadedAuras, _auraReferences, id, out aura);

    /// <summary>Leaves out the action id's record loaded so far -- a save's that failed its checks -- and loads in its place the lower layer's record it replaced; false when there is none that decodes.</summary>
    public bool TryLoadActionBeneath(Guid id, out ActionDefinition action) => TryLoadBeneath(IndexesNow.Actions, _loadedActions, _auraReferences, id, out action);

    /// <summary>The item id names, as TryLoadActionBeneath.</summary>
    public bool TryLoadItemBeneath(Guid id, out ItemDefinition item) => TryLoadBeneath(IndexesNow.Items, _loadedItems, _auraReferences, id, out item);

    /// <summary>The aura id names, as TryLoadActionBeneath: what decodes it afterwards, whatever names it, gets the lower layer's.</summary>
    public bool TryLoadAuraBeneath(Guid id, out AuraDefinition aura) => TryLoadBeneath(IndexesNow.Auras, _loadedAuras, _auraReferences, id, out aura);

    /// <summary>Whether a layer holds action id (or what its redirects lead to) enabled, without decoding it.</summary>
    public bool HasAction(Guid id) => IndexesNow.Actions.TryResolve(id, out _);

    /// <summary>Whether a layer holds item id (or what its redirects lead to) enabled, without decoding it.</summary>
    public bool HasItem(Guid id) => IndexesNow.Items.TryResolve(id, out _);

    /// <summary>Whether a layer holds aura id (or what its redirects lead to) enabled, without decoding it.</summary>
    public bool HasAura(Guid id) => IndexesNow.Auras.TryResolve(id, out _);

    /// <summary>Whether table tableName's record id (or what its redirects lead to) has been decoded -- by a TryLoad or a Prefetch -- or failed to be; false for a Guid no layer holds.</summary>
    public bool IsDecoded(string tableName, Guid id) =>
        IndexOf(tableName) is { } index && index(IndexesNow).TryResolve(id, out var entry) && LoadedTableOf(tableName).IsDecoded(entry.Id);

    /// <summary>The Guid of every record of table tableName that layer wins, in index order, without decoding any; empty for an area table.</summary>
    public IReadOnlyList<Guid> RecordIdsIn(ContentLayer layer, string tableName) =>
        IndexOf(tableName) is { } index ? [.. index(IndexesNow).Entries.Where(entry => ReferenceEquals(entry.Layer, layer)).Select(entry => entry.Id)] : [];

    /// <summary>The Guid of every record of table tableName that layer wins over a lower layer's record of the same Guid, in index order, without decoding any; empty for an area table.</summary>
    public IReadOnlyList<Guid> OverridingRecordIdsIn(ContentLayer layer, string tableName) =>
        IndexOf(tableName) is { } index ? [.. index(IndexesNow).Entries.Where(entry => ReferenceEquals(entry.Layer, layer) && entry.Beneath is not null).Select(entry => entry.Id)] : [];

    /// <summary>Decodes each of actionIds and itemIds that isn't yet, so a later TryLoad finds it at once. Safe from any thread: a worker planning a neighborhood calls it.</summary>
    public void Prefetch(IEnumerable<Guid> actionIds, IEnumerable<Guid> itemIds)
    {
        foreach (var id in actionIds)
        {
            TryLoadAction(id, out _);
        }

        foreach (var id in itemIds)
        {
            TryLoadItem(id, out _);
        }
    }

    /// <summary>Where table tableName's record id was read from, once that table has been read or indexed; false for a Guid it doesn't hold.</summary>
    public bool TryGetLocation(string tableName, Guid id, out ContentRecordLocation location)
    {
        location = null!;
        if (IndexOf(tableName) is { } index)
        {
            if (!index(IndexesNow).TryGet(id, out var indexEntry))
            {
                return false;
            }

            var entry = LoadedTableOf(tableName).EntryOf(indexEntry);
            location = new ContentRecordLocation(entry.Layer, entry.File, entry.Line);
            return true;
        }

        lock (_failures)
        {
            return _locationsByTable.TryGetValue(tableName, out var locations) && locations.TryGetValue(id, out location!);
        }
    }

    /// <summary>For table tableName in layer: how many records the layer wins, how many of those are decoded so far, and how many of its lines failed and were left out.</summary>
    /// <remarks>An area table is read whole, so all it holds is decoded; an action, item or aura table counts a record decoded once something asked for it.</remarks>
    public ContentTableCounts CountIn(ContentLayer layer, string tableName)
    {
        int records;
        int loaded;
        if (IndexOf(tableName) is { } index)
        {
            var loadedTable = LoadedTableOf(tableName);
            var entries = index(IndexesNow).Entries.Where(entry => ReferenceEquals(loadedTable.EntryOf(entry).Layer, layer)).ToList();
            records = entries.Count;
            loaded = entries.Count(entry => loadedTable.IsLoaded(entry.Id));
        }
        else
        {
            lock (_failures)
            {
                records = _locationsByTable.TryGetValue(tableName, out var locations) ? locations.Values.Count(location => ReferenceEquals(location.Layer, layer)) : 0;
            }

            loaded = records;
        }

        var leftOut = Failures.Count(failure => failure.Layer == layer.Name && IsFileOf(tableName, failure.File));
        return new ContentTableCounts(records, loaded, leftOut);
    }

    /// <summary>The file table tableName is read from; throws for Areas, which is a folder of files.</summary>
    public static string FileNameOf(string tableName) => tableName switch
    {
        ActionsTable => ActionsFileName,
        ItemsTable => ItemsFileName,
        AurasTable => AurasFileName,
        AreaPalettesTable => AreaPalettesFileName,
        AreaPlacementsTable => AreaPlacementsFileName,
        RedirectsTable => RedirectsFileName,
        _ => throw new ArgumentException($"{tableName} isn't one file.", nameof(tableName)),
    };

    /// <summary>Whether table tableName is read from file, a path inside a layer.</summary>
    public static bool IsFileOf(string tableName, string file) => tableName switch
    {
        ActionsTable => file == ActionsFileName,
        ItemsTable => file == ItemsFileName,
        AurasTable => file == AurasFileName,
        AreaPalettesTable => file == AreaPalettesFileName,
        AreaPlacementsTable => file == AreaPlacementsFileName,
        RedirectsTable => file == RedirectsFileName,
        AreasTable => file.StartsWith(AreasDirectoryName + "/", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>Throws when any record read so far failed in a layer a game can't start without: master or a mod.</summary>
    public void ThrowIfFailed()
    {
        var stopping = Failures.Where(StopsTheGame).ToList();
        if (stopping.Count > 0)
        {
            throw new InvalidOperationException($"The content has records that can't be loaded:{Environment.NewLine}{string.Join(Environment.NewLine, stopping)}");
        }
    }

    private void ThrowIfStopping()
    {
        if (_throwsOnFailure && Volatile.Read(ref _hasStoppingFailure))
        {
            ThrowIfFailed();
        }
    }

    private static Func<Indexes, ContentIndex<DefinitionSummary>>? IndexOf(string tableName) => tableName switch
    {
        ActionsTable => static indexes => indexes.Actions,
        ItemsTable => static indexes => indexes.Items,
        AurasTable => static indexes => indexes.Auras,
        _ => null,
    };

    private ILoadedTable LoadedTableOf(string tableName) => tableName switch
    {
        ActionsTable => _loadedActions,
        ItemsTable => _loadedItems,
        _ => _loadedAuras,
    };

    private Indexes ScanIndexes()
    {
        var failures = new List<ContentFailure>();
        var auras = _layers.IndexTable(AurasFileName, AurasTable, Summarize, failures);
        var actions = _layers.IndexTable(ActionsFileName, ActionsTable, Summarize, failures);
        var items = _layers.IndexTable(ItemsFileName, ItemsTable, Summarize, failures);
        AddFailures(failures);

        return new Indexes(auras, actions, items);
    }

    /// <summary>What the summary holds of one enabled record's raw JSON: anything unreadable reads as its default, since decoding is what reports it.</summary>
    private static DefinitionSummary Summarize(JsonElement record)
    {
        ContentGuid.TryParse(record.GetProperty(ContentTable.IdKey).GetString()!, out var id);
        var name = record.TryGetProperty(NameKey, out var nameElement) && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString()! : string.Empty;
        var spriteName = record.TryGetProperty(SpriteNameKey, out var spriteElement) && spriteElement.ValueKind == JsonValueKind.String ? spriteElement.GetString() : null;
        var canTrade = !record.TryGetProperty(CanTradeKey, out var tradeElement) || tradeElement.ValueKind != JsonValueKind.False;
        var hasContents = record.TryGetProperty(ContentsKey, out _);

        var tags = new List<GameplayTag>();
        if (record.TryGetProperty(TagsKey, out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagsElement.EnumerateArray())
            {
                if (tag.ValueKind == JsonValueKind.String && GameplayTag.TryGet(tag.GetString()!, out var gameplayTag))
                {
                    tags.Add(gameplayTag);
                }
            }
        }

        return new DefinitionSummary(id, name, spriteName, GameplayTagSet.Create(tags.ToArray()), canTrade, hasContents);
    }

    private bool TryLoad<T>(ContentIndex<DefinitionSummary> index, LoadedTable<T> loaded, IContentReferences references, Guid id, out T value)
        where T : class
    {
        if (!index.TryResolve(id, out var entry))
        {
            value = null!;
            return false;
        }

        if (loaded.TryGet(entry.Id, out value))
        {
            return true;
        }

        lock (_loadLock)
        {
            if (loaded.TryGet(entry.Id, out value))
            {
                return true;
            }

            if (loaded.IsDecoded(entry.Id))
            {
                value = null!;
                return false;
            }

            return TryDecodeDownFrom(entry, loaded, references, out value);
        }
    }

    private bool TryLoadBeneath<T>(ContentIndex<DefinitionSummary> index, LoadedTable<T> loaded, IContentReferences references, Guid id, out T value)
        where T : class
    {
        if (!index.TryResolve(id, out var entry))
        {
            value = null!;
            return false;
        }

        lock (_loadLock)
        {
            if (loaded.EntryOf(entry).Beneath is not { } beneath)
            {
                loaded.Fail(entry.Id);
                value = null!;
                return false;
            }

            return TryDecodeDownFrom(beneath, loaded, references, out value);
        }
    }

    /// <summary>Decodes entry, else each lower layer's record it replaced in turn, so a failing record is left out as a whole-table read leaves it out; called under the load lock.</summary>
    private bool TryDecodeDownFrom<T>(ContentIndexEntry<DefinitionSummary> entry, LoadedTable<T> loaded, IContentReferences references, out T value)
        where T : class
    {
        for (ContentIndexEntry<DefinitionSummary>? candidate = entry; candidate is not null; candidate = candidate.Beneath)
        {
            var failures = new List<ContentFailure>();
            var decoded = ContentLayers.Load<T, DefinitionSummary>(candidate, _types, references, failures);
            AddFailures(failures);

            if (decoded is not null)
            {
                loaded.Add(candidate, decoded);
                value = decoded;
                return true;
            }

            ThrowIfStopping();
        }

        loaded.Fail(entry.Id);
        value = null!;
        return false;
    }

    private IReadOnlyList<T> LoadAll<T>(ContentIndex<DefinitionSummary> index, TryLoadDefinition<T> tryLoad)
        where T : class
    {
        var values = new List<T>(index.Count);
        foreach (var entry in index.Entries)
        {
            if (tryLoad(entry.Id, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private LayeredRecords<T> ReadTable<T>(string fileName, string tableName)
        where T : class =>
        Read(failures => _layers.ReadTable<T>(fileName, tableName, _types, NoContentReferences.Instance, failures), tableName);

    private LayeredRecords<AreaDefinition> ReadAreas() =>
        Read(failures => _layers.ReadDocuments<AreaDefinition>(AreasDirectoryName, AreaFileExtension, AreasTable, _types, NoContentReferences.Instance, failures), AreasTable);

    /// <summary>Reads one whole table, remembers where each of its records came from, and throws if it failed where that stops the build.</summary>
    private LayeredRecords<T> Read<T>(Func<List<ContentFailure>, LayeredRecords<T>> read, string tableName)
        where T : class
    {
        var failures = new List<ContentFailure>();
        var records = read(failures);
        var locations = new Dictionary<Guid, ContentRecordLocation>(records.Records.Count);
        foreach (var record in records.Records)
        {
            locations[record.Id] = LocationOf(record);
        }

        lock (_failures)
        {
            _locationsByTable[tableName] = locations;
        }

        AddFailures(failures);
        ThrowIfStopping();
        return records;
    }

    /// <summary>Keeps failures, noting whether any of them stops the game, so a lookup checks one flag rather than every failure.</summary>
    private void AddFailures(List<ContentFailure> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        lock (_failures)
        {
            _failures.AddRange(failures);
        }

        if (failures.Exists(StopsTheGame))
        {
            Volatile.Write(ref _hasStoppingFailure, true);
        }
    }

    private bool StopsTheGame(ContentFailure failure) =>
        failure.Layer is null || !_layers.TryGetLayer(failure.Layer, out var layer) || layer.Kind is ContentLayerKind.Master or ContentLayerKind.Mod;

    private static ContentRecordLocation LocationOf<T>(ContentLayerRecord<T> record)
        where T : class =>
        new(record.Layer, record.File, record.Line, record.Source);

    private static List<LocatedRecord<T>> Located<T>(LayeredRecords<T> records)
        where T : class =>
        [.. records.Records.Select(record => new LocatedRecord<T>(record.Value, LocationOf(record)))];

    /// <summary>Writes auras, actions and items to directory's three tables, canonically and in the order given -- the master layer's files.</summary>
    /// <remarks>Disabled records already in a table are kept as written, after the enabled ones, so re-exporting never drops them.</remarks>
    public static void WriteMaster(string directory, IEnumerable<AuraDefinition> auras, IEnumerable<ActionDefinition> actions, IEnumerable<ItemDefinition> items, ContentTypeRegistry types)
    {
        Directory.CreateDirectory(directory);
        WriteTable(Path.Combine(directory, AurasFileName), AurasTable, auras, types);
        WriteTable(Path.Combine(directory, ActionsFileName), ActionsTable, actions, types);
        WriteTable(Path.Combine(directory, ItemsFileName), ItemsTable, items, types);
    }

    private static void WriteTable<T>(string path, string tableName, IEnumerable<T> records, ContentTypeRegistry types)
        where T : notnull
    {
        var text = new StringBuilder(ContentTable.Write(tableName, records, types));
        foreach (var disabledLine in DisabledLines(path))
        {
            text.Append(disabledLine).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>The disabled records' lines already in path, as written.</summary>
    private static IEnumerable<string> DisabledLines(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path).Skip(1).Where(line => line.Contains($"\"{ContentTable.DisabledKey}\":true", StringComparison.Ordinal));
    }

    private delegate bool TryLoadDefinition<T>(Guid id, out T value);

    private sealed record Indexes(ContentIndex<DefinitionSummary> Auras, ContentIndex<DefinitionSummary> Actions, ContentIndex<DefinitionSummary> Items)
    {
        public IReadOnlyList<DefinitionSummary> ActionSummaries { get; } = [.. Actions.Entries.Select(entry => entry.Summary).OrderBy(summary => summary.Id)];

        public IReadOnlyList<DefinitionSummary> ItemSummaries { get; } = [.. Items.Entries.Select(entry => entry.Summary).OrderBy(summary => summary.Id)];
    }

    private interface ILoadedTable
    {
        bool IsLoaded(Guid id);

        bool IsDecoded(Guid id);

        ContentIndexEntry<DefinitionSummary> EntryOf(ContentIndexEntry<DefinitionSummary> indexEntry);
    }

    /// <summary>The records of one table decoded so far, by Guid, each with the entry it was decoded from, and the ones that failed to, so neither is tried again. Read without a lock; written under GameContent's.</summary>
    private sealed class LoadedTable<T> : ILoadedTable
        where T : class
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, T?> _valuesById = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, ContentIndexEntry<DefinitionSummary>> _entriesById = new();

        private int _count;

        public int Count => _count;

        public bool IsLoaded(Guid id) => _valuesById.TryGetValue(id, out var value) && value is not null;

        public bool IsDecoded(Guid id) => _valuesById.ContainsKey(id);

        public bool TryGet(Guid id, out T value)
        {
            var found = _valuesById.TryGetValue(id, out var stored);
            value = stored!;
            return found && stored is not null;
        }

        /// <summary>The entry indexEntry's record was decoded from: itself, or a lower layer's it fell back to.</summary>
        public ContentIndexEntry<DefinitionSummary> EntryOf(ContentIndexEntry<DefinitionSummary> indexEntry) =>
            _entriesById.TryGetValue(indexEntry.Id, out var used) ? used : indexEntry;

        public void Add(ContentIndexEntry<DefinitionSummary> entry, T value)
        {
            var wasLoaded = IsLoaded(entry.Id);
            _entriesById[entry.Id] = entry;
            _valuesById[entry.Id] = value;
            if (!wasLoaded)
            {
                Interlocked.Increment(ref _count);
            }
        }

        public void Fail(Guid id)
        {
            if (IsLoaded(id))
            {
                Interlocked.Decrement(ref _count);
            }

            _valuesById[id] = null;
        }
    }

    /// <summary>An action's, item's or aura's auras, resolved by Guid through the content itself: each decoded with whatever names it.</summary>
    private sealed class AuraReferences(GameContent content) : IContentReferences
    {
        public bool TryResolve<T>(Guid id, out T value)
            where T : class
        {
            if (typeof(T) == typeof(AuraDefinition) && content.TryLoadAura(id, out var aura))
            {
                value = (T)(object)aura;
                return true;
            }

            value = null!;
            return false;
        }
    }
}

/// <summary>What a whole-catalog question needs of one action or item, read from its line without decoding it: its Guid, name, sprite, declared tags, whether it trades and whether it holds contents.</summary>
/// <remarks>Tags are the declared ones (an activator's implied tags aren't in the line). An action trades and holds nothing.</remarks>
public sealed record DefinitionSummary(Guid Id, string Name, string? SpriteName, GameplayTagSet DeclaredTags, bool CanTrade, bool HasContents);

/// <summary>One table's count in one layer: the records the layer wins, how many are decoded so far, and how many of its lines failed and were left out.</summary>
public readonly record struct ContentTableCounts(int Records, int Loaded, int LeftOut);
