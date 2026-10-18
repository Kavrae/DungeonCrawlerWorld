using System.Text;
using Engine.Content;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Inventory;

namespace Game.Content;

/// <summary>The master manifest: the shipped actions, items and auras, one JSON Lines table each.</summary>
/// <remarks>
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

    public const string ActionsFileName = "Actions.jsonl";
    public const string ItemsFileName = "Items.jsonl";
    public const string AurasFileName = "Auras.jsonl";

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

    /// <summary>Reads all three tables under directory now, collecting every failure in Failures rather than throwing; a table whose file is missing is empty.</summary>
    public static MasterContent Load(string directory, ContentTypeRegistry types)
    {
        var content = new MasterContent(directory, types, throwsOnFailure: false);
        _ = content.Auras;
        _ = content.Actions;
        _ = content.Items;
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
