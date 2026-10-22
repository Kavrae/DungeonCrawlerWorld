using System.Text;
using Engine.Content;
using Game.Areas;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Inventory;

namespace Game.Content;

/// <summary>The master manifest: the shipped actions, items, auras, area palettes and area placements, one JSON Lines table each, and areas, one file each.</summary>
/// <remarks>
/// Areas are in the Areas folder, one ContentDocument per file read in file name order, so an area's rows are one per
/// line; they, their palettes and placements are read with where each record came from (LocatedRecord), for the
/// build's area checks to name the row and column a failure is at.
///
/// Each table is read the first time it is asked for, the auras before whichever of actions or items asks first, since
/// those name auras by Guid -- so a build of modules that never asks for items never needs the item codec. Each table's
/// records keep their file order, which is the order the catalogs register them in. A disabled record isn't loaded;
/// when it names a replacement, its Guid redirects to that record (AuraRedirects, ActionRedirects, ItemRedirects), so
/// content and C# that name it resolve to the replacement. A record that fails is a ContentFailure: the game's copy must
/// load whole, so one opened to throw (the game's) stops the build on the table that failed, and one loaded for a tool
/// collects every failure (Failures). The game reads the copy under its output directory (Content.csproj copies
/// Definitions/Master only, never Definitions/Drafts).
/// </remarks>
public sealed class MasterContent
{
    public const string ActionsTable = "Actions";
    public const string ItemsTable = "Items";
    public const string AurasTable = "Auras";

    public const string AreasTable = "Areas";
    public const string AreaPalettesTable = "AreaPalettes";
    public const string AreaPlacementsTable = "AreaPlacements";

    public const string ActionsFileName = "Actions.jsonl";
    public const string ItemsFileName = "Items.jsonl";
    public const string AurasFileName = "Auras.jsonl";
    public const string AreaPalettesFileName = "AreaPalettes.jsonl";
    public const string AreaPlacementsFileName = "AreaPlacements.jsonl";

    /// <summary>The folder of area files, one area each (an AreasTable document).</summary>
    public const string AreasDirectoryName = "Areas";

    /// <summary>An area file's extension.</summary>
    public const string AreaFilePattern = "*.json";

    private readonly string _directory;
    private readonly ContentTypeRegistry _types;
    private readonly bool _throwsOnFailure;
    private readonly List<ContentFailure> _failures = [];
    private readonly Dictionary<Guid, Guid> _auraRedirects = [];
    private readonly Dictionary<Guid, Guid> _actionRedirects = [];
    private readonly Dictionary<Guid, Guid> _itemRedirects = [];
    private readonly AuraReferences _auraReferences;

    private IReadOnlyList<AuraDefinition>? _auras;
    private IReadOnlyList<ActionDefinition>? _actions;
    private IReadOnlyList<ItemDefinition>? _items;
    private IReadOnlyList<LocatedRecord<AreaDefinition>>? _areas;
    private IReadOnlyList<LocatedRecord<AreaPalette>>? _areaPalettes;
    private IReadOnlyList<LocatedRecord<AreaPlacement>>? _areaPlacements;
    private readonly Dictionary<Guid, Guid> _areaRedirects = [];
    private readonly Dictionary<Guid, Guid> _areaPaletteRedirects = [];
    private readonly Dictionary<Guid, Guid> _areaPlacementRedirects = [];

    private MasterContent(string directory, ContentTypeRegistry types, bool throwsOnFailure)
    {
        _directory = directory;
        _types = types;
        _throwsOnFailure = throwsOnFailure;
        _auraReferences = new AuraReferences(_auraRedirects);
    }

    /// <summary>Where the game's own build output keeps the master manifest.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "Definitions", "Master");

    public IReadOnlyList<AuraDefinition> Auras => _auras ??= LoadAuras();

    public IReadOnlyList<ActionDefinition> Actions => _actions ??= LoadTable<ActionDefinition>(ActionsFileName, ActionsTable, _actionRedirects);

    public IReadOnlyList<ItemDefinition> Items => _items ??= LoadTable<ItemDefinition>(ItemsFileName, ItemsTable, _itemRedirects);

    /// <summary>Every enabled area, one per file of the Areas folder (in file name order), with where it was read from.</summary>
    public IReadOnlyList<LocatedRecord<AreaDefinition>> Areas => _areas ??= LoadAreas();

    /// <summary>Every enabled area palette, in file order, with where it was read from.</summary>
    public IReadOnlyList<LocatedRecord<AreaPalette>> AreaPalettes => _areaPalettes ??= ReadLocatedTable<AreaPalette>(AreaPalettesFileName, AreaPalettesTable, _areaPaletteRedirects);

    /// <summary>Every enabled fixed area placement, in file order, with where it was read from.</summary>
    public IReadOnlyList<LocatedRecord<AreaPlacement>> AreaPlacements => _areaPlacements ??= ReadLocatedTable<AreaPlacement>(AreaPlacementsFileName, AreaPlacementsTable, _areaPlacementRedirects);

    /// <summary>Each disabled area that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> AreaRedirects
    {
        get
        {
            _ = Areas;
            return _areaRedirects;
        }
    }

    /// <summary>Each disabled area palette that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> AreaPaletteRedirects
    {
        get
        {
            _ = AreaPalettes;
            return _areaPaletteRedirects;
        }
    }

    /// <summary>Each disabled area placement that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> AreaPlacementRedirects
    {
        get
        {
            _ = AreaPlacements;
            return _areaPlacementRedirects;
        }
    }

    /// <summary>Each disabled aura that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> AuraRedirects
    {
        get
        {
            _ = Auras;
            return _auraRedirects;
        }
    }

    /// <summary>Each disabled action that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> ActionRedirects
    {
        get
        {
            _ = Actions;
            return _actionRedirects;
        }
    }

    /// <summary>Each disabled item that names a replacement, from its Guid to the replacement's.</summary>
    public IReadOnlyDictionary<Guid, Guid> ItemRedirects
    {
        get
        {
            _ = Items;
            return _itemRedirects;
        }
    }

    /// <summary>Every record of the tables read so far that couldn't be loaded.</summary>
    public IReadOnlyList<ContentFailure> Failures => _failures;

    /// <summary>The manifest under directory, each table read when first asked for; a table with a record that fails throws then.</summary>
    public static MasterContent Open(string directory, ContentTypeRegistry types) => new(directory, types, throwsOnFailure: true);

    /// <summary>The manifest under directory, each table read when first asked for, a record that fails left out and kept in Failures -- for a tool's build, which shows what is wrong instead of stopping.</summary>
    public static MasterContent OpenCollectingFailures(string directory, ContentTypeRegistry types) => new(directory, types, throwsOnFailure: false);

    /// <summary>Reads every table under directory now, collecting every failure in Failures rather than throwing; a table whose file is missing is empty.</summary>
    public static MasterContent Load(string directory, ContentTypeRegistry types)
    {
        var content = new MasterContent(directory, types, throwsOnFailure: false);
        _ = content.Auras;
        _ = content.Actions;
        _ = content.Items;
        _ = content.AreaPalettes;
        _ = content.AreaPlacements;
        _ = content.Areas;
        return content;
    }

    /// <summary>Throws when any table read so far had a record that failed.</summary>
    public void ThrowIfFailed()
    {
        if (_failures.Count > 0)
        {
            throw new InvalidOperationException($"The master manifest has records that can't be loaded:{Environment.NewLine}{string.Join(Environment.NewLine, _failures)}");
        }
    }

    private IReadOnlyList<AuraDefinition> LoadAuras()
    {
        var auras = ReadTable<AuraDefinition>(AurasFileName, AurasTable, _auraRedirects);
        foreach (var aura in auras)
        {
            _auraReferences.Add(aura);
        }

        return auras;
    }

    private IReadOnlyList<T> LoadTable<T>(string fileName, string tableName, Dictionary<Guid, Guid> redirects)
        where T : class
    {
        _ = Auras;
        return ReadTable<T>(fileName, tableName, redirects);
    }

    private List<T> ReadTable<T>(string fileName, string tableName, Dictionary<Guid, Guid> redirects)
        where T : class
    {
        var failureCount = _failures.Count;
        var records = Loaded(ContentTable.Read<T>(Path.Combine(_directory, fileName), tableName, _types, _auraReferences, _failures), redirects);
        if (_throwsOnFailure && _failures.Count > failureCount)
        {
            ThrowIfFailed();
        }

        return records;
    }

    private List<LocatedRecord<T>> ReadLocatedTable<T>(string fileName, string tableName, Dictionary<Guid, Guid> redirects)
        where T : class
    {
        var failureCount = _failures.Count;
        var located = new List<LocatedRecord<T>>();
        foreach (var record in ContentTable.Read<T>(Path.Combine(_directory, fileName), tableName, _types, NoContentReferences.Instance, _failures))
        {
            if (Remember(record.Id, record.IsDisabled, record.Replacement, redirects))
            {
                located.Add(new LocatedRecord<T>(record.Value!, new ContentRecordLocation(fileName, record.Line)));
            }
        }

        ThrowIfFailedSince(failureCount);
        return located;
    }

    private List<LocatedRecord<AreaDefinition>> LoadAreas()
    {
        var failureCount = _failures.Count;
        var located = new List<LocatedRecord<AreaDefinition>>();
        var areasDirectory = Path.Combine(_directory, AreasDirectoryName);
        if (!Directory.Exists(areasDirectory))
        {
            return located;
        }

        var paths = Directory.GetFiles(areasDirectory, AreaFilePattern);
        Array.Sort(paths, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var fileName = $"{AreasDirectoryName}/{Path.GetFileName(path)}";
            var record = ContentDocument.Read<AreaDefinition>(File.ReadAllText(path, Encoding.UTF8), fileName, AreasTable, _types, NoContentReferences.Instance, _failures);
            if (record is not null && Remember(record.Id, record.IsDisabled, record.Replacement, _areaRedirects))
            {
                located.Add(new LocatedRecord<AreaDefinition>(record.Value!, new ContentRecordLocation(fileName, 1, record.Source)));
            }
        }

        ThrowIfFailedSince(failureCount);
        return located;
    }

    /// <summary>Whether the record is enabled; a disabled one's replacement, if any, is kept as its redirect.</summary>
    private static bool Remember(Guid id, bool isDisabled, Guid? replacement, Dictionary<Guid, Guid> redirects)
    {
        if (isDisabled)
        {
            if (replacement is { } replacementId)
            {
                redirects[id] = replacementId;
            }

            return false;
        }

        return true;
    }

    private void ThrowIfFailedSince(int failureCount)
    {
        if (_throwsOnFailure && _failures.Count > failureCount)
        {
            ThrowIfFailed();
        }
    }

    /// <summary>Writes actions and items, and every aura they name, to directory's three tables, canonically and in the order given (auras in the order first named).</summary>
    /// <remarks>Disabled records already in a table are kept as written, after the enabled ones, so re-exporting never drops them.</remarks>
    public static void Write(string directory, IEnumerable<ActionDefinition> actions, IEnumerable<ItemDefinition> items, ContentTypeRegistry types)
    {
        Directory.CreateDirectory(directory);
        var actionList = actions.ToList();
        var itemList = items.ToList();

        WriteTable(Path.Combine(directory, AurasFileName), AurasTable, AurasNamedBy(actionList, itemList), types);
        WriteTable(Path.Combine(directory, ActionsFileName), ActionsTable, actionList, types);
        WriteTable(Path.Combine(directory, ItemsFileName), ItemsTable, itemList, types);
    }

    /// <summary>Every aura an entry of actions or items names, each once, in the order first named.</summary>
    public static IReadOnlyList<AuraDefinition> AurasNamedBy(IEnumerable<ActionDefinition> actions, IEnumerable<ItemDefinition> items)
    {
        var auras = new List<AuraDefinition>();
        var seen = new HashSet<Guid>();
        void Collect(IEffectEntry entry)
        {
            foreach (var aura in entry.ReferencedAuras)
            {
                if (seen.Add(aura.Id))
                {
                    auras.Add(aura);
                }
            }
        }

        foreach (var action in actions)
        {
            EffectEntries.ForEach(action, Collect);
        }

        foreach (var item in items)
        {
            EffectEntries.ForEach(item, Collect);
        }

        return auras;
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

    private static List<T> Loaded<T>(IReadOnlyList<ContentTableRecord<T>> records, Dictionary<Guid, Guid> redirects)
        where T : class
    {
        var loaded = new List<T>(records.Count);
        foreach (var record in records)
        {
            if (record.IsDisabled)
            {
                if (record.Replacement is { } replacement)
                {
                    redirects[record.Id] = replacement;
                }

                continue;
            }

            loaded.Add(record.Value!);
        }

        return loaded;
    }

    /// <summary>The auras loaded so far, by Guid, a disabled one's Guid resolving to its replacement.</summary>
    private sealed class AuraReferences(IReadOnlyDictionary<Guid, Guid> redirects) : IContentReferences
    {
        private readonly Dictionary<Guid, AuraDefinition> _aurasById = [];

        public void Add(AuraDefinition aura) => _aurasById[aura.Id] = aura;

        public bool TryResolve<T>(Guid id, out T value)
            where T : class
        {
            if (typeof(T) == typeof(AuraDefinition) && _aurasById.TryGetValue(redirects.TryGetValue(id, out var replacement) ? replacement : id, out var aura))
            {
                value = (T)(object)aura;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
