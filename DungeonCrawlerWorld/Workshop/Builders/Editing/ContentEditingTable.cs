using System.Text.Json;
using Engine.Content;
using Workshop;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>What the workspace asks of a table it holds open, whatever its definitions' type.</summary>
public interface IContentEditingTable
{
    string TableName { get; }

    /// <summary>The tab that shows its records.</summary>
    string TabTitle { get; }

    bool Contains(Guid id);

    /// <summary>The record's name as a browser shows it; its Guid when it has none.</summary>
    string NameOf(Guid id);

    /// <summary>The JSON of the record now.</summary>
    string JsonOf(Guid id);

    /// <summary>The type its codec reads a record as.</summary>
    Type ValueType { get; }

    /// <summary>The definition id last decoded to; null for a record not held.</summary>
    object? DefinitionOf(Guid id);

    /// <summary>Whether id is a mod's record, shown and never edited.</summary>
    bool IsReadOnly(Guid id);

    /// <summary>Puts json back into the record id -- what undo and redo do -- and evaluates it again; null takes the record away, and json for a record not held makes it again.</summary>
    void Restore(Guid id, string? json);

    /// <summary>Takes id away as a step of the workspace's log; false for a record not held. The workspace decides first whether it may be.</summary>
    bool Delete(Guid id, string description);

    /// <summary>Its records as a reference picker lists them.</summary>
    IReadOnlyList<ContentReferenceChoice> ReferenceChoices { get; }

    /// <summary>How each of its records looks on the map and in a list (glyph, colour, sprite); none for a table whose records have no look.</summary>
    IEnumerable<ContentAppearance> Appearances { get; }

    /// <summary>Checks again what only the other records can say -- every record's look against every other's, and what each names that is disabled -- after a record of any table changed.</summary>
    void RefreshCrossRecordProblems();

    /// <summary>Whether id is held and disabled ("off").</summary>
    bool IsDisabled(Guid id);

    /// <summary>What replaces id while it is disabled; null for nothing, or a record not held.</summary>
    Guid? ReplacementOf(Guid id);

    /// <summary>The records that may replace a disabled one: published, enabled, and not exceptId.</summary>
    IReadOnlyList<ContentReferenceChoice> ReplacementChoices(Guid exceptId);

    /// <summary>Applies change to id's record outside the inspector -- the workspace's own steps (Disable, Cascade) -- and evaluates it again; the caller records the step.</summary>
    void Change(Guid id, Action<EditableContentRecord> change);

    /// <summary>Whether something changed that no save has written: a record, or a record taken away.</summary>
    bool HasChanges { get; }

    /// <summary>Whether a publish has something to put in master: HasChanges, or a draft that would publish (not one with errors, which stays a draft whatever is published).</summary>
    bool HasUnpublishedChanges { get; }

    /// <summary>What a save writes of it: every record that changed, and when publishing every unheld draft too -- or, given onlyIds, those records alone.</summary>
    IReadOnlyList<ContentSaveEntry> SaveEntries(IReadOnlySet<Guid>? onlyIds, bool publishing);

    /// <summary>The records taken away (Delete, a New undone) that a publish takes out of master and Drafts.</summary>
    IReadOnlyList<Guid> PendingRemovals { get; }

    /// <summary>The records taken away that master doesn't hold, which a save to Drafts takes out of Drafts.</summary>
    IReadOnlyList<Guid> PendingDraftRemovals { get; }

    /// <summary>The published records taken away and not yet saved so, which a save to Drafts records there as removal lines.</summary>
    IReadOnlyList<Guid> PendingRemovalLines { get; }

    /// <summary>Whether master holds id as saved, and the record is still held.</summary>
    bool IsPublished(Guid id);

    /// <summary>Whether id is deleted and its deletion isn't final yet -- a master record not yet published so, a draft not yet saved so: listed as Deleted, and restorable.</summary>
    bool IsDeleted(Guid id);

    /// <summary>After a save wrote the files: each record is what it was saved as, each removal is gone from them, and each of removalLines is saved as deleted -- kept out of master by a publish when removalReasons gives why.</summary>
    void CompleteSave(IReadOnlyList<ContentSaveResult> results, IReadOnlyList<Guid> removed, IReadOnlyList<Guid> removalLines, IReadOnlyDictionary<Guid, IReadOnlyList<string>> removalReasons);

    /// <summary>Its records that name tableName's id, with the field each names it from.</summary>
    IEnumerable<(Guid Id, string Name, string FieldPath)> ReferencesTo(string tableName, Guid id);

    /// <summary>What id names as it is now, saved or not, with the field naming each; none for a record not held.</summary>
    IReadOnlyList<ContentFieldReference> ReferencesOf(Guid id);

    /// <summary>Evaluates every record again -- after the session it is checked against was rebuilt.</summary>
    void EvaluateAll();
}

/// <summary>How a record looks: its glyph and glyph colour, and its sprite, named for the warning that another record looks the same.</summary>
public readonly record struct ContentAppearance(Guid Id, string Name, string Glyph, uint GlyphColor, string? SpriteName);

/// <summary>Where a record's look is held: the keys of its glyph and its sprite, which a warning that it looks like another is shown at.</summary>
public sealed record ContentAppearanceFields(string GlyphKey, string SpriteKey);

/// <summary>
/// One table's records held open for editing, one ContentRecordDocument per Guid: each master record as its line, each
/// draft as saved there (a master record a mod replaces in the game included), a record only a mod has as the session
/// decoded it -- and evaluated after every edit: its values checked against
/// the schemas, decoded through the real codec, and the definition put through the per-record checks.
/// </summary>
/// <remarks>
/// <para>
/// A master line the session left out (it didn't decode) and a draft that doesn't decode are opened all the same, previewed
/// as a stand-in (placeholderJsonOf) until they decode. A record whose glyph and colour, or sprite, another record of any
/// table also has is warned about at that field -- the look is still allowed. A failed check is located at the field its
/// message names (ContentProblemLocator), so the Error List can take the player there.
/// </para>
/// <para>
/// The table only answers what a save needs (SaveEntries, PendingRemovals) and takes in what it did (CompleteSave): the
/// workspace routes and writes. Without files (Files null, a test) every record opened is taken as master's.
/// </para>
/// </remarks>
public sealed class ContentEditingTable<T> : IContentEditingTable
    where T : class
{
    private static readonly IReadOnlySet<string> EnvelopeKeys = new HashSet<string>(StringComparer.Ordinal) { ContentTable.DisabledKey, ContentTable.ReplacementKey };

    private readonly ContentWorkspace _workspace;
    private readonly Func<T, string> _nameOf;
    private readonly Func<T, IReadOnlyList<string>> _checks;
    private readonly Func<T, ContentAppearance>? _appearanceOf;
    private readonly ContentAppearanceFields? _appearanceFields;
    private readonly Func<EditableContentRecord, string>? _placeholderJsonOf;
    private readonly List<ContentRecordDocument<T>> _documents = [];
    private readonly Dictionary<Guid, ContentRecordDocument<T>> _documentsById = [];

    /// <summary>Records taken away by a step (a New undone, a Delete), kept so redo brings back the same record -- what it was opened as, and how it is saved.</summary>
    private readonly Dictionary<Guid, ContentRecordDocument<T>> _removed = [];

    /// <param name="modNameOf">The mod a definition's winning record came from; null for master.</param>
    /// <param name="appearanceOf">How a definition looks, for the warning that two records look the same; null for a table whose records have no look.</param>
    /// <param name="appearanceFields">Where that look is held in a record, for appearanceOf.</param>
    /// <param name="placeholderJsonOf">A record that decodes, standing in for one that doesn't (its name and glyph, read from it): what the browser and preview show of it until it decodes.</param>
    public ContentEditingTable(ContentWorkspace workspace, string tableName, string tabTitle, IEnumerable<T> definitions, Func<T, string> nameOf, Func<T, string?> modNameOf, Func<T, IReadOnlyList<string>> checks, Func<T, ContentAppearance>? appearanceOf = null, ContentAppearanceFields? appearanceFields = null, Func<EditableContentRecord, string>? placeholderJsonOf = null)
    {
        _workspace = workspace;
        TableName = tableName;
        TabTitle = tabTitle;
        _nameOf = nameOf;
        _checks = checks;
        _appearanceOf = appearanceOf;
        _appearanceFields = appearanceFields;
        _placeholderJsonOf = placeholderJsonOf;
        Files = workspace.FilesOf(tableName);
        Open(definitions, modNameOf);
        workspace.Register(this);
    }

    public string TableName { get; }

    public string TabTitle { get; }

    /// <summary>Where the table is saved; null when the workspace saves nothing.</summary>
    public ContentTableFiles? Files { get; }

    public IReadOnlyList<ContentRecordDocument<T>> Documents => _documents;

    /// <summary>The records deleted whose deletion isn't final yet (IsDeleted): shown greyed as Deleted until it is, and restorable until then.</summary>
    public IReadOnlyList<ContentRecordDocument<T>> DeletedDocuments => [.. _removed.Values.Where(IsDeletionPending)];

    /// <summary>A record taken away that a save or publish still has to take out of the files: master holds it, or Drafts does.</summary>
    private static bool IsDeletionPending(ContentRecordDocument<T> document) => document.IsInMaster || document.IsDraft;

    public Type ValueType => typeof(T);

    /// <summary>Changes whenever any record's problems may have.</summary>
    public int ProblemsVersion { get; private set; }

    public bool HasChanges =>
        _documents.Any(document => !document.IsReadOnly && document.IsChanged) || _removed.Values.Any(IsRemovalUnsaved);

    public bool HasUnpublishedChanges =>
        HasChanges || _documents.Any(document => !document.IsReadOnly && document.IsWaitingToPublish) || _removed.Values.Any(document => document.IsInMaster);

    /// <summary>A record taken away that no save has written so: a draft still in Drafts, or a published record with no removal line yet.</summary>
    private static bool IsRemovalUnsaved(ContentRecordDocument<T> document) =>
        document.IsInMaster ? !document.IsRemovalSaved : document.IsDraft;

    /// <summary>Raised after a record was evaluated again: an edit, an undo, a redo or a save.</summary>
    public event Action<ContentRecordDocument<T>, bool>? DocumentChanged;

    /// <summary>Raised after a record was taken away -- a New undone, a Delete -- and after a save wrote its deletion.</summary>
    public event Action<ContentRecordDocument<T>>? DocumentRemoved;

    public IEnumerable<ContentAppearance> Appearances =>
        _appearanceOf is { } appearanceOf ? _documents.Select(document => appearanceOf(document.LastGood)) : [];

    public IReadOnlyList<ContentReferenceChoice> ReferenceChoices =>
        [.. _documents.Select(document => new ContentReferenceChoice(document.Id, _nameOf(document.LastGood))).OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<Guid> PendingRemovals =>
        [.. _removed.Values.Where(document => document.IsInMaster || document.IsDraft).Select(document => document.Id)];

    public IReadOnlyList<Guid> PendingDraftRemovals =>
        [.. _removed.Values.Where(document => !document.IsInMaster && document.IsDraft).Select(document => document.Id)];

    public IReadOnlyList<Guid> PendingRemovalLines =>
        [.. _removed.Values.Where(document => document.IsInMaster && !document.IsRemovalSaved).Select(document => document.Id)];

    public ContentRecordDocument<T>? Find(Guid id) => _documentsById.GetValueOrDefault(id);

    public bool Contains(Guid id) => _documentsById.ContainsKey(id);

    public bool IsPublished(Guid id) => Find(id) is { IsInMaster: true };

    public bool IsDeleted(Guid id) => _removed.TryGetValue(id, out var document) && IsDeletionPending(document);

    /// <summary>Whether the deletion of a deleted record (IsDeleted) is unsaved: a save would still write it.</summary>
    public bool IsDeletionUnsaved(ContentRecordDocument<T> document) => _removed.ContainsKey(document.Id) && IsRemovalUnsaved(document);

    /// <summary>
    /// What is true of document now: the one rule its row's markers and the browser's filters both read. A deleted
    /// record is Deleted, Unsaved while its deletion is, HasErrors while the last save kept the deletion back, and
    /// DiffersFromMaster while master holds it; a mod's record is none of these.
    /// </summary>
    public ContentRecordState StateOf(ContentRecordDocument<T> document)
    {
        if (document.IsReadOnly)
        {
            return ContentRecordState.None;
        }

        if (IsDeleted(document.Id))
        {
            return ContentRecordState.Deleted
                | (IsDeletionUnsaved(document) ? ContentRecordState.Unsaved : ContentRecordState.None)
                | (document.SaveProblems.Count > 0 ? ContentRecordState.HasErrors : ContentRecordState.None)
                | (document.IsInMaster ? ContentRecordState.DiffersFromMaster : ContentRecordState.None);
        }

        return (document.IsChanged ? ContentRecordState.Unsaved : ContentRecordState.None)
            | (document.IsDraft || !document.IsInMaster ? ContentRecordState.Draft : ContentRecordState.None)
            | (document.IsDisabled ? ContentRecordState.Disabled : ContentRecordState.None)
            | (document.HasErrors || document.SaveProblems.Count > 0 ? ContentRecordState.HasErrors : ContentRecordState.None)
            | (DiffersFromMaster(document) ? ContentRecordState.DiffersFromMaster : ContentRecordState.None);
    }

    /// <summary>Whether a publish would write document, held and not deleted: any value differing from master's line, or -- master not holding it -- anything saved or changed.</summary>
    private bool DiffersFromMaster(ContentRecordDocument<T> document) =>
        document.DifferencesFromMaster(_workspace.Types) is { } differences ? differences.Count > 0 : document.IsChanged || document.IsDraft;

    public string NameOf(Guid id) => (Find(id) ?? _removed.GetValueOrDefault(id)) is { } document ? _nameOf(document.LastGood) : ContentGuid.Format(id);

    public string JsonOf(Guid id) => _documentsById[id].Record.Json;

    public object? DefinitionOf(Guid id) => Find(id)?.LastGood;

    public bool IsReadOnly(Guid id) => (Find(id) ?? _removed.GetValueOrDefault(id)) is { IsReadOnly: true };

    /// <summary>Opens every record: the session's definitions (each master one as its master line, or its draft when it has one), then each master line the session left out, then each draft of a record of its own; a published record Drafts holds a removal line for is opened as taken away.</summary>
    private void Open(IEnumerable<T> definitions, Func<T, string?> modNameOf)
    {
        var master = Files?.ReadMaster();
        var drafts = new Dictionary<Guid, string>();
        var removalLines = new HashSet<Guid>();
        foreach (var line in Files?.ReadDrafts().Lines ?? [])
        {
            if (!ContentTableFiles.TryReadDraftLine(line, out var id, out var json, out var isRemoval))
            {
                continue;
            }

            if (isRemoval)
            {
                removalLines.Add(id);
                continue;
            }

            drafts[id] = json;
        }

        OpenRecords(definitions, modNameOf, master, drafts);
        foreach (var id in removalLines)
        {
            if (Find(id) is { IsInMaster: true } removed)
            {
                removed.IsRemovalSaved = true;
                _removed[id] = removed;
                _documents.Remove(removed);
                _documentsById.Remove(id);
            }
        }
    }

    /// <remarks>
    /// One record per Guid, and always the one the Workshop writes: a definition a mod won is opened as the mod's own,
    /// read-only, only when neither master nor Drafts holds its Guid. Otherwise master's record -- its draft, else its line
    /// -- is opened in its place, editable, naming the mod the game loads instead; the mod's stands in only when master's
    /// can't be opened at all.
    /// </remarks>
    private void OpenRecords(IEnumerable<T> definitions, Func<T, string?> modNameOf, ContentTableLines? master, Dictionary<Guid, string> drafts)
    {
        var replacedInGame = new Dictionary<Guid, (T Definition, string ModName, string Written)>();
        foreach (var definition in definitions)
        {
            var modName = modNameOf(definition);
            var written = ContentTable.WriteLine(definition, _workspace.Types);
            var id = ContentTable.TryReadId(written, out var writtenId) ? writtenId : Guid.Empty;
            if (modName is not null && (master?.Contains(id) == true || drafts.ContainsKey(id)))
            {
                replacedInGame[id] = (definition, modName, written);
                continue;
            }

            string? masterLine = master is null ? (modName is null ? written : null) : master.TryGetLine(id, out var line) && !ContentTableFiles.IsDisabledLine(line) ? line : null;
            var draft = modName is null && drafts.Remove(id, out var draftJson) ? draftJson : null;
            var json = draft ?? (modName is null ? masterLine ?? written : written);
            Add(new ContentRecordDocument<T>(new EditableContentRecord(TableName, typeof(T), json), definition, modName)
            {
                MasterLine = masterLine,
                DraftJson = draft,
            });
        }

        foreach (var line in master?.Lines ?? [])
        {
            if (!ContentTable.TryReadId(line, out var id) || Contains(id))
            {
                continue;
            }

            var draft = drafts.Remove(id, out var draftJson) ? draftJson : null;
            OpenUndecoded(draft ?? line, masterLine: line, draft, ModReplacing(replacedInGame, id));
        }

        foreach (var (id, draft) in drafts)
        {
            OpenUndecoded(draft, masterLine: null, draft, ModReplacing(replacedInGame, id));
        }

        foreach (var (id, (definition, modName, written)) in replacedInGame)
        {
            if (!Contains(id))
            {
                Add(new ContentRecordDocument<T>(new EditableContentRecord(TableName, typeof(T), written), definition, modName));
            }
        }
    }

    private static string? ModReplacing(Dictionary<Guid, (T Definition, string ModName, string Written)> replacedInGame, Guid id) =>
        replacedInGame.TryGetValue(id, out var replaced) ? replaced.ModName : null;

    /// <summary>Opens json, which the session didn't decode (a master line it left out or a mod replaced, or a draft), with what it decodes to now or a stand-in.</summary>
    private void OpenUndecoded(string json, string? masterLine, string? draftJson, string? replacedInGameBy)
    {
        var record = new EditableContentRecord(TableName, typeof(T), json);
        if ((Decode(json) ?? (_placeholderJsonOf is { } placeholderOf ? Decode(placeholderOf(record)) : null)) is not { } original)
        {
            return;
        }

        Add(new ContentRecordDocument<T>(record, original, modName: null)
        {
            MasterLine = masterLine,
            DraftJson = draftJson,
            ReplacedInGameBy = replacedInGameBy,
        });
    }

    private void Add(ContentRecordDocument<T> document)
    {
        _documents.Add(document);
        _documentsById[document.Id] = document;
    }

    public void Restore(Guid id, string? json)
    {
        if (Find(id) is not { } document)
        {
            if (json is null)
            {
                return;
            }

            if (_removed.Remove(id, out var removed))
            {
                removed.Record.Restore(json);
                Readd(removed);
            }
            else if (Decode(json) is { } definition)
            {
                AddDocument(EditableContentRecord.CreateNew(TableName, typeof(T), json), definition);
            }

            return;
        }

        if (json is null)
        {
            RemoveDocument(document);
            return;
        }

        document.Record.Restore(json);
        document.SaveProblems = [];
        EvaluateEdited(document);
        DocumentChanged?.Invoke(document, true);
    }

    /// <summary>Applies edit to document through the workspace's log, evaluates it again, and tells the tab.</summary>
    public void Apply(ContentRecordDocument<T> document, ContentInspectorEdit edit)
    {
        _workspace.Record(this, document.Record, edit);
        document.SaveProblems = [];
        EvaluateEdited(document);
        DocumentChanged?.Invoke(document, edit.ChangesShape);
    }

    /// <summary>Evaluates document after an edit or an undo: each required value it newly left missing isn't filled in yet, and isn't shown as a problem until the record is saved.</summary>
    private void EvaluateEdited(ContentRecordDocument<T> document)
    {
        var missingBefore = document.MissingPaths();
        EvaluateAlone(document);
        document.TrackNotFilledInYet(missingBefore);
        _workspace.RefreshCrossRecordProblems();
    }

    /// <summary>Makes a new record holding json -- a step of its own in the workspace's log, undone by taking the record away -- and returns it; null when json doesn't decode.</summary>
    public ContentRecordDocument<T>? CreateNew(string json, string description)
    {
        if (Decode(json) is not { } definition)
        {
            return null;
        }

        var document = AddDocument(EditableContentRecord.CreateNew(TableName, typeof(T), json), definition);
        _workspace.RecordCreation(this, document.Record, description);
        return document;
    }

    public void Change(Guid id, Action<EditableContentRecord> change)
    {
        if (Find(id) is not { } document)
        {
            return;
        }

        change(document.Record);
        document.SaveProblems = [];
        EvaluateEdited(document);
        DocumentChanged?.Invoke(document, true);
    }

    public bool IsDisabled(Guid id) => Find(id) is { IsDisabled: true };

    public Guid? ReplacementOf(Guid id) => Find(id)?.Replacement;

    public IReadOnlyList<ContentReferenceChoice> ReplacementChoices(Guid exceptId) =>
        [.. _documents
            .Where(document => document.Id != exceptId && document.IsInMaster && !document.IsDisabled)
            .Select(document => new ContentReferenceChoice(document.Id, _nameOf(document.LastGood)))
            .OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Takes id away -- a step of its own, undone by bringing it back; it stays listed as Deleted until a publish takes it out of master (a draft: until a save takes it out of Drafts).</summary>
    /// <remarks>Whether it may be is the workspace's to say (ContentWorkspace.DeleteRefusal); this only takes it away.</remarks>
    public bool Delete(Guid id, string description)
    {
        if (Find(id) is not { } document)
        {
            return false;
        }

        _workspace.RecordRemoval(this, document.Record, description);
        RemoveDocument(document);
        return true;
    }

    /// <summary>Brings a deleted record back (IsDeleted) -- a step of its own, undone by deleting it again; false when it isn't deleted.</summary>
    public bool RestoreDeleted(ContentRecordDocument<T> document, string description)
    {
        if (!IsDeleted(document.Id) || !_removed.Remove(document.Id))
        {
            return false;
        }

        Readd(document);
        _workspace.RecordCreation(this, document.Record, description);
        return true;
    }

    private T? Decode(string json) => Decode(json, out _);

    /// <summary>What json decodes to through the table's codec, as a record of it is decoded; null, with failure saying why, when it doesn't.</summary>
    public T? Decode(string json, out string? failure)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            failure = null;
            return ContentObject.Read<T>(document.RootElement, _workspace.Types, _workspace, EnvelopeKeys);
        }
        catch (Exception exception) when (exception is ContentFormatException or InvalidOperationException or ArgumentException or FormatException or OverflowException or JsonException)
        {
            failure = exception.Message;
            return null;
        }
    }

    private ContentRecordDocument<T> AddDocument(EditableContentRecord record, T definition)
    {
        var document = new ContentRecordDocument<T>(record, definition, modName: null);
        Add(document);
        Evaluate(document);
        DocumentChanged?.Invoke(document, true);
        return document;
    }

    /// <summary>A record taken away earlier back in the list, evaluated again -- unsaved when Drafts holds its removal line, so a save takes the line back out.</summary>
    private void Readd(ContentRecordDocument<T> document)
    {
        if (document.IsRemovalSaved)
        {
            document.Record.MarkNew();
        }

        document.SaveProblems = [];
        Add(document);
        Evaluate(document);
        DocumentChanged?.Invoke(document, true);
    }

    private void RemoveDocument(ContentRecordDocument<T> document)
    {
        document.SaveProblems = [];
        _removed[document.Id] = document;
        _documents.Remove(document);
        _documentsById.Remove(document.Id);
        ProblemsVersion++;
        _workspace.RefreshCrossRecordProblems();
        DocumentRemoved?.Invoke(document);
    }

    /// <summary>Evaluates every record -- when the workspace opens, and after the session is rebuilt.</summary>
    public void EvaluateAll()
    {
        foreach (var document in _documents)
        {
            EvaluateAlone(document);
        }

        _workspace.RefreshCrossRecordProblems();
    }

    /// <summary>Checks the record's values, decodes it through the real codec, and checks what it decoded to; then every record's look against every other's.</summary>
    public void Evaluate(ContentRecordDocument<T> document)
    {
        EvaluateAlone(document);
        _workspace.RefreshCrossRecordProblems();
    }

    public void RefreshCrossRecordProblems()
    {
        foreach (var document in _documents)
        {
            var problems = ReferenceProblemsOf(document);
            if (!problems.SequenceEqual(document.ReferenceProblems))
            {
                document.ReferenceProblems = problems;
                ProblemsVersion++;
            }
        }

        if (_appearanceOf is not { } appearanceOf || _appearanceFields is not { } fields)
        {
            return;
        }

        var looks = new AppearanceIndex(_workspace.AllAppearances());
        foreach (var document in _documents)
        {
            var warnings = AppearanceWarnings(appearanceOf(document.LastGood), looks, fields);
            if (!warnings.SequenceEqual(document.AppearanceProblems))
            {
                document.AppearanceProblems = warnings;
                ProblemsVersion++;
            }
        }
    }

    public IReadOnlyList<ContentSaveEntry> SaveEntries(IReadOnlySet<Guid>? onlyIds, bool publishing) =>
        [.. _documents
            .Where(document => !document.IsReadOnly && (onlyIds is null ? document.IsChanged || (publishing && document.IsDraft) : onlyIds.Contains(document.Id)))
            .Select(document => new ContentSaveEntry(
                TableName,
                document.Id,
                _nameOf(document.LastGood),
                document.HasErrors,
                document.ReferencesNamed(_workspace.Types),
                document.DecodeFailure is not null ? null
                    : document.IsDisabled ? ContentTable.WriteDisabled(document.LastGood, document.Replacement, _workspace.Types)
                    : ContentTable.WriteLine(document.LastGood, _workspace.Types),
                document.Record.Json,
                document.MasterLine))];

    public void CompleteSave(IReadOnlyList<ContentSaveResult> results, IReadOnlyList<Guid> removed, IReadOnlyList<Guid> removalLines, IReadOnlyDictionary<Guid, IReadOnlyList<string>> removalReasons)
    {
        foreach (var result in results)
        {
            if (Find(result.Id) is not { } document)
            {
                continue;
            }

            var rebuilds = false;
            if (result.Destination == ContentSaveDestination.Master)
            {
                if (result.PublishedLine is { } line && document.Record.Json != line)
                {
                    document.Record.Restore(line);
                    rebuilds = true;
                }

                document.MasterLine = result.PublishedLine;
                document.DraftJson = null;
            }
            else
            {
                document.DraftJson = document.MasterLine is { } masterLine && ContentTableFiles.IsSameRecord(masterLine, document.Record.Json) ? null : document.Record.Json;
            }

            document.IsRemovalSaved = false;
            document.Record.MarkUnchanged();
            document.ShowEverythingMissing();
            document.SaveProblems = result.Reasons;
            document.MasterMovedTo = result.IsMasterMoved ? new MovedMasterLine(result.MasterLineNow) : null;
            if (rebuilds)
            {
                EvaluateAlone(document);
            }

            DocumentChanged?.Invoke(document, rebuilds);
        }

        foreach (var id in removed)
        {
            if (_removed.TryGetValue(id, out var document))
            {
                document.MasterLine = null;
                document.DraftJson = null;
                document.IsRemovalSaved = false;
                document.SaveProblems = [];
                document.Record.MarkNew();
                DocumentRemoved?.Invoke(document);
            }
        }

        foreach (var id in removalLines)
        {
            if (_removed.TryGetValue(id, out var document))
            {
                document.IsRemovalSaved = true;
                document.DraftJson = null;
                document.SaveProblems = removalReasons.GetValueOrDefault(id) ?? [];
                DocumentRemoved?.Invoke(document);
            }
        }

        ProblemsVersion++;
    }

    /// <summary>
    /// Takes the master line a publish found on disk (MasterMovedTo) as what id is compared with and published over from
    /// now on -- its edits kept, its markers and Changes then against master as it is -- and clears that publish's
    /// refusal; false when no publish found master moved under it.
    /// </summary>
    public bool ReloadMaster(Guid id)
    {
        if (Find(id) is not { MasterMovedTo: { } moved } document)
        {
            return false;
        }

        document.MasterLine = moved.Line;
        document.MasterMovedTo = null;
        document.SaveProblems = [];
        ProblemsVersion++;
        DocumentChanged?.Invoke(document, true);
        return true;
    }

    public IReadOnlyList<ContentFieldReference> ReferencesOf(Guid id) =>
        Find(id) is { } document ? document.ReferencesNamed(_workspace.Types) : [];

    public IEnumerable<(Guid Id, string Name, string FieldPath)> ReferencesTo(string tableName, Guid id)
    {
        foreach (var document in _documents)
        {
            foreach (var reference in document.ReferencesNamed(_workspace.Types))
            {
                if (reference.TableName == tableName && reference.Id == id)
                {
                    yield return (document.Id, _nameOf(document.LastGood), reference.DisplayPath);
                }
            }
        }
    }

    /// <summary>A disabled record's replacement that isn't a published record of this table, or whose chain comes back round to it; an enabled record's reference to a record disabled with nothing at the end of its replacements.</summary>
    private IReadOnlyList<ContentFieldProblem> ReferenceProblemsOf(ContentRecordDocument<T> document)
    {
        if (document.IsDisabled)
        {
            return document.Replacement is { } replacement && _workspace.ReplacementProblem(this, document.Id, replacement) is { } problem
                ? [new ContentFieldProblem(ContentPath.Root.Key(ContentTable.ReplacementKey), "Replacement", problem)]
                : [];
        }

        List<ContentFieldProblem>? problems = null;
        foreach (var reference in document.ReferencesNamed(_workspace.Types))
        {
            if (_workspace.ResolvesToNothing(reference.TableName, reference.Id) is { } name)
            {
                (problems ??= []).Add(new ContentFieldProblem(reference.Path, reference.DisplayPath, $"Names {name}, which is disabled with nothing replacing it."));
            }
        }

        return problems ?? [];
    }

    /// <summary>A warning at the glyph when another record has the same glyph and colour, and at the sprite when another has the same sprite.</summary>
    private static IReadOnlyList<ContentFieldProblem> AppearanceWarnings(ContentAppearance appearance, AppearanceIndex looks, ContentAppearanceFields fields)
    {
        if (appearance.Glyph.Length == 0 && appearance.SpriteName is not { Length: > 0 })
        {
            return [];
        }

        var problems = new List<ContentFieldProblem>();
        if (appearance.Glyph.Length > 0)
        {
            var sameGlyph = looks.ByGlyph[(appearance.Glyph, appearance.GlyphColor)].Where(other => other.Id != appearance.Id).Select(other => other.Name).ToList();
            if (sameGlyph.Count > 0)
            {
                problems.Add(new ContentFieldProblem(ContentPath.Root.Key(fields.GlyphKey), "Glyph", $"Glyph \"{appearance.Glyph}\" in this colour is also used by {NameList(sameGlyph)}.", IsWarning: true));
            }
        }

        if (appearance.SpriteName is { Length: > 0 } spriteName)
        {
            var sameSprite = looks.BySprite[spriteName].Where(other => other.Id != appearance.Id).Select(other => other.Name).ToList();
            if (sameSprite.Count > 0)
            {
                problems.Add(new ContentFieldProblem(ContentPath.Root.Key(fields.SpriteKey), "Sprite name", $"Sprite \"{spriteName}\" is also used by {NameList(sameSprite)}.", IsWarning: true));
            }
        }

        return problems;
    }

    /// <summary>Every record's look grouped by glyph and colour, and by sprite: built once per check, so each record is compared with its own group alone.</summary>
    private sealed class AppearanceIndex(IEnumerable<ContentAppearance> appearances)
    {
        private readonly List<ContentAppearance> _appearances = [.. appearances];
        private ILookup<(string, uint), ContentAppearance>? _byGlyph;
        private ILookup<string, ContentAppearance>? _bySprite;

        public ILookup<(string Glyph, uint GlyphColor), ContentAppearance> ByGlyph => _byGlyph ??= _appearances.Where(look => look.Glyph.Length > 0).ToLookup(look => (look.Glyph, look.GlyphColor));

        public ILookup<string, ContentAppearance> BySprite => _bySprite ??= _appearances.Where(look => look.SpriteName is { Length: > 0 }).ToLookup(look => look.SpriteName!, StringComparer.Ordinal);
    }

    private static string NameList(IReadOnlyList<string> names) =>
        names.Count <= 3 ? string.Join(", ", names) : $"{string.Join(", ", names.Take(3))} and {names.Count - 3} more";

    private void EvaluateAlone(ContentRecordDocument<T> document)
    {
        ProblemsVersion++;
        document.SchemaProblems = ContentSchemaValidation.Validate(document.Record, _workspace.Types, _workspace);
        try
        {
            using var json = JsonDocument.Parse(document.Record.Json);
            var decoded = ContentObject.Read<T>(json.RootElement, _workspace.Types, _workspace, EnvelopeKeys);
            document.LastGood = decoded;
            document.DecodeFailure = null;
            document.CheckProblems = _checks(decoded);
        }
        catch (Exception exception) when (exception is ContentFormatException or InvalidOperationException or ArgumentException or FormatException or OverflowException or JsonException)
        {
            document.DecodeFailure = exception.Message;
            document.CheckProblems = [];
        }
    }

    private const string ChangedFromMasterNote = " (changed from master)";

    /// <summary>
    /// To Publish's lines for the table: each record held that differs from master -- new, changed, disabled or enabled --
    /// then each master record deleted and not yet published so; each with where it stands: unsaved, saved as a draft,
    /// kept out by its errors or by the last publish (why), or with master moved under it. A record edited back to master
    /// is no line, nor is a mod's.
    /// </summary>
    public IEnumerable<WorkshopPendingRecord> PendingPublish()
    {
        foreach (var document in _documents)
        {
            if (!StateOf(document).HasFlag(ContentRecordState.DiffersFromMaster))
            {
                continue;
            }

            yield return new WorkshopPendingRecord(_nameOf(document.LastGood), TableName, PublishChangeOf(document), PublishStateOf(document)) { TabTitle = TabTitle, RecordId = document.Id };
        }

        foreach (var document in _removed.Values.Where(document => IsDeleted(document.Id) && StateOf(document).HasFlag(ContentRecordState.DiffersFromMaster)))
        {
            var state = !document.IsRemovalSaved ? "Unsaved"
                : document.SaveProblems.Count > 0 ? $"Kept in drafts: {document.SaveProblems[0]}"
                : "Deletion saved as draft";
            yield return new WorkshopPendingRecord(_nameOf(document.LastGood), TableName, "Deleted", state) { TabTitle = TabTitle, RecordId = document.Id };
        }
    }

    /// <summary>What publishing document, which differs from master, would change there: "New", "Disabled"/"Enabled" and the count of other changes.</summary>
    private string PublishChangeOf(ContentRecordDocument<T> document)
    {
        if (document.DifferencesFromMaster(_workspace.Types) is not { } differences)
        {
            return "New";
        }

        var parts = new List<string>(2);
        if (differences.Any(difference => difference.Path.Text == ContentTable.DisabledKey))
        {
            parts.Add(document.IsDisabled ? "Disabled" : "Enabled");
        }

        var otherCount = differences.Count(difference => difference.Path.Text is not (ContentTable.DisabledKey or ContentTable.ReplacementKey));
        if (otherCount > 0)
        {
            parts.Add(otherCount == 1 ? "1 change" : $"{otherCount} changes");
        }

        return parts.Count == 0 ? "Replacement changed" : string.Join(", ", parts);
    }

    private static string PublishStateOf(ContentRecordDocument<T> document) =>
        document.MasterMovedTo is not null ? "Master changed on disk: Reload Master first"
        : document.HasErrors ? "Has errors: stays a draft"
        : document.SaveProblems.Count > 0 ? $"Kept in drafts: {document.SaveProblems[0]}"
        : document.IsChanged ? "Unsaved"
        : "Saved as draft";

    /// <summary>
    /// The Error List's lines for every record: each field's problem at its field, then a failed decode a field problem
    /// doesn't already explain, then each failed check, then why the last save kept it in Drafts. A problem at a field
    /// that differs from master says so.
    /// </summary>
    public IEnumerable<WorkshopProblem> Problems()
    {
        foreach (var document in _documents)
        {
            var name = _nameOf(document.LastGood);
            foreach (var problem in document.FieldProblems)
            {
                var changedNote = document.IsChangedFromMaster(problem.Path, _workspace.Types) ? ChangedFromMasterNote : string.Empty;
                yield return new WorkshopProblem(problem.IsWarning ? WorkshopProblemSeverity.Warning : WorkshopProblemSeverity.Error, $"{name}: {problem.DisplayPath}: {problem.Message}{changedNote}", TableName)
                {
                    TabTitle = TabTitle,
                    RecordId = document.Id,
                    FieldPath = problem.Path.Text,
                };
            }

            if (document.DecodeFailure is { } failure && !document.ExplainsDecodeFailure)
            {
                yield return new WorkshopProblem(WorkshopProblemSeverity.Error, $"{name}: {failure}", TableName) { TabTitle = TabTitle, RecordId = document.Id };
            }

            foreach (var check in document.CheckProblems)
            {
                yield return new WorkshopProblem(WorkshopProblemSeverity.Error, check, TableName)
                {
                    TabTitle = TabTitle,
                    RecordId = document.Id,
                    FieldPath = ContentProblemLocator.Locate(document.Record, _workspace.Types, check)?.Text,
                };
            }

            foreach (var reason in document.SaveProblems)
            {
                yield return new WorkshopProblem(WorkshopProblemSeverity.Error, $"{name}: kept in Drafts: {reason}", TableName) { TabTitle = TabTitle, RecordId = document.Id };
            }
        }

        foreach (var document in DeletedDocuments)
        {
            foreach (var reason in document.SaveProblems)
            {
                yield return new WorkshopProblem(WorkshopProblemSeverity.Error, $"{_nameOf(document.LastGood)}: deletion kept in Drafts: {reason}", TableName) { TabTitle = TabTitle, RecordId = document.Id };
            }
        }
    }
}
