using System.Text;
using Engine.Content;
using Engine.Persistence;

namespace Game.Content;

/// <summary>What a ContentStore call did with a definition.</summary>
public enum ContentStoreOutcome
{
    /// <summary>Appended to the save's definitions: a new definition is registered at once, an override takes effect when the next session loads the save.</summary>
    Saved,

    /// <summary>The same definition is already saved -- a new one registered under its derived Guid, or an override in force with this same line; nothing was written.</summary>
    AlreadySaved,

    /// <summary>It failed its checks, so it was appended to the save's drafts and not registered.</summary>
    SavedAsDraft,

    /// <summary>A draft that still fails its checks, or doesn't decode, kept in the drafts as it is.</summary>
    KeptAsDraft,

    /// <summary>The save's override was taken away: the lower layer's definition is back when the next session loads the save.</summary>
    Removed,

    /// <summary>Asked to remove an override the save doesn't hold; nothing was written.</summary>
    NothingToRemove,

    /// <summary>Asked to override a Guid no master or mod record has; nothing was written.</summary>
    Refused,
}

/// <summary>What a ContentStore call did: the outcome, the definition it ended with and its Guid, and every check it failed.</summary>
/// <param name="Definition">The definition saved (Saved, AlreadySaved), the lower layer's definition the next session gets back (Removed, when it loads), or the draft as written (SavedAsDraft); null when there is none (a draft that doesn't decode, Refused, NothingToRemove).</param>
/// <param name="Id">The Guid it is saved or drafted under.</param>
/// <param name="Problems">Every check it failed, or why the call was refused; empty otherwise.</param>
public sealed record ContentStoreResult<T>(ContentStoreOutcome Outcome, T? Definition, Guid Id, IReadOnlyList<string> Problems)
    where T : class;

/// <summary>One table of a session's save -- actions, items or auras -- and the catalog its definitions are registered in.</summary>
/// <remarks>
/// <para>
/// A new definition's Guid is derived from what it is (Decision 11): its canonical line, written with an empty id, hashed
/// under the table's namespace, so making the same definition twice gives one record and one line. An override keeps the
/// Guid of the master or mod record it overrides. Either is checked first (ContentChecks); one that passes is appended to
/// the save's Definitions table. One that fails is appended to the save's Drafts table under a working Guid (a random one
/// for a new definition, its target's for an override) and registered nowhere.
/// </para>
/// <para>
/// A definition never changes during a session: what a Guid means is fixed when the session loads. So a new definition
/// is an addition, registered at once (its line queued first, before anything can hold it), while an override or the
/// removal of one only writes the save, and takes effect -- everywhere the Guid is used -- when the next session loads it.
/// </para>
/// <para>
/// A draft is kept as written, whether or not it decodes. Publishing reads the drafts again: one that now passes is saved
/// as a new definition (taking its derived Guid) or as an override (keeping its target's), and its lines leave the
/// drafts; one that doesn't stays.
/// </para>
/// </remarks>
public sealed class ContentStoreTable<T>
    where T : class
{
    private readonly ContentStoreTableRules<T> _rules;
    private readonly SaveFolder _save;
    private readonly ContentTypeRegistry _types;
    private readonly IContentReferences _references;
    private readonly ContentStoreTableRules<T>.TryGetDefinition _tryGetLower;
    private readonly HashSet<Guid> _overriddenIds;
    private Dictionary<Guid, string>? _lastDefinitionLines;

    internal ContentStoreTable(ContentStoreTableRules<T> rules, SaveFolder save, ContentTypeRegistry types, IContentReferences references, ContentStoreTableRules<T>.TryGetDefinition tryGetLower, IEnumerable<Guid> overriddenIds)
    {
        _rules = rules;
        _save = save;
        _types = types;
        _references = references;
        _tryGetLower = tryGetLower;
        _overriddenIds = [.. overriddenIds];
    }

    public string TableName => _rules.TableName;

    public string DefinitionsPath => _save.DefinitionsPathOf(_rules.FileName);

    public string DraftsPath => _save.DraftsPathOf(_rules.FileName);

    /// <summary>The Guids the save overrides in this table, now.</summary>
    public IReadOnlyCollection<Guid> OverriddenIds => _overriddenIds;

    /// <summary>The master or mod definition of id, under any override the save holds; false when no lower layer has one.</summary>
    public bool TryGetLower(Guid id, out T definition) => _tryGetLower(id, out definition);

    /// <summary>The Guid CreatePersistent gives definition: a hash of its canonical line with an empty id, whatever its own id is.</summary>
    public Guid DerivedIdOf(T definition) =>
        ContentGuid.Derive(_rules.IdNamespace, ContentWriter.WriteRecord(_types, _rules.WithId(definition, Guid.Empty)));

    /// <summary>Saves definition as a new definition under its derived Guid: the one already registered there when it is known, otherwise checked, appended and registered -- or, failing its checks, saved as a draft.</summary>
    public ContentStoreResult<T> CreatePersistent(T definition)
    {
        var id = DerivedIdOf(definition);
        if (_rules.TryGetRegistered(id, out var registered))
        {
            return new ContentStoreResult<T>(ContentStoreOutcome.AlreadySaved, registered, id, []);
        }

        var record = _rules.WithId(definition, id);
        var problems = _rules.ProblemsOf(record);
        if (problems.Count > 0)
        {
            var draft = _rules.WithId(definition, Guid.NewGuid());
            AppendDraft(draft);
            return new ContentStoreResult<T>(ContentStoreOutcome.SavedAsDraft, draft, _rules.IdOf(draft), problems);
        }

        AppendDefinition(record);
        _rules.Register(record);
        return new ContentStoreResult<T>(ContentStoreOutcome.Saved, record, id, []);
    }

    /// <summary>Saves definition as the save's override of the master or mod record holding its Guid, for the next session: checked and appended -- or, failing its checks, saved as a draft under that Guid. This session keeps the definition it loaded.</summary>
    /// <remarks>An override still in force whose line is already this one is AlreadySaved and nothing is appended; any other, one a removal line took away included, is appended again, so the save's tables only ever grow at their end.</remarks>
    public ContentStoreResult<T> Override(T definition)
    {
        var id = _rules.IdOf(definition);
        if (!_tryGetLower(id, out _))
        {
            return new ContentStoreResult<T>(ContentStoreOutcome.Refused, null, id, [$"No master or mod {_rules.RecordNoun} has the Guid {id} to override."]);
        }

        var problems = _rules.ProblemsOf(definition);
        if (problems.Count > 0)
        {
            AppendDraft(definition);
            return new ContentStoreResult<T>(ContentStoreOutcome.SavedAsDraft, definition, id, problems);
        }

        var line = ContentTable.WriteLine(definition, _types);
        if (_overriddenIds.Contains(id) && line == LastDefinitionLineOf(id))
        {
            return new ContentStoreResult<T>(ContentStoreOutcome.AlreadySaved, definition, id, []);
        }

        AppendDefinitionLine(id, line);
        _overriddenIds.Add(id);
        return new ContentStoreResult<T>(ContentStoreOutcome.Saved, definition, id, []);
    }

    /// <summary>Takes the save's override of id away -- a removal line in its definitions -- so the next session loads the master or mod definition. This session keeps the definition it loaded.</summary>
    public ContentStoreResult<T> RemoveOverride(Guid id)
    {
        if (!_overriddenIds.Contains(id))
        {
            return new ContentStoreResult<T>(ContentStoreOutcome.NothingToRemove, null, id, [$"The save doesn't override the {_rules.RecordNoun} {id}."]);
        }

        _save.Writer.Append(DefinitionsPath, Header, ContentTable.WriteRemoval(id));
        _overriddenIds.Remove(id);
        return new ContentStoreResult<T>(ContentStoreOutcome.Removed, _tryGetLower(id, out var lower) ? lower : null, id, []);
    }

    /// <summary>Appends definition to the drafts under its own Guid -- a new working Guid when it has none -- whether or not it passes, with every check it fails.</summary>
    public ContentStoreResult<T> SaveDraft(T definition)
    {
        var draft = _rules.IdOf(definition) == Guid.Empty ? _rules.WithId(definition, Guid.NewGuid()) : definition;
        AppendDraft(draft);
        return new ContentStoreResult<T>(ContentStoreOutcome.SavedAsDraft, draft, _rules.IdOf(draft), _rules.ProblemsOf(draft));
    }

    /// <summary>The working Guid of every draft in the drafts table, in the order each first appears; a line with no readable Guid isn't one.</summary>
    public IReadOnlyList<Guid> DraftIds()
    {
        _save.Writer.Flush();
        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var line in DraftLines())
        {
            if (ContentTable.TryReadId(line, out var id) && seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Publishes the draft under workingId if it now passes: a new definition takes its derived Guid, an override keeps its target's, and its lines leave the drafts. Kept as it is when it doesn't decode or still fails.</summary>
    public ContentStoreResult<T> TryPublishDraft(Guid workingId)
    {
        _save.Writer.Flush();
        var failures = new List<ContentFailure>();
        var draftsText = File.Exists(DraftsPath) ? File.ReadAllText(DraftsPath, Encoding.UTF8) : string.Empty;
        var file = ContentTable.Read<T>(draftsText, _rules.FileName, _rules.TableName, _types, _references, failures);

        var lastFailure = failures.LastOrDefault(failure => failure.RecordId == workingId);
        var lastRecord = file.Records.LastOrDefault(record => record.Id == workingId && record.Value is not null);
        if (lastRecord is null || (lastFailure is not null && lastFailure.Line > lastRecord.Line))
        {
            var reason = lastFailure?.ToString() ?? $"No draft has the Guid {workingId}.";
            return new ContentStoreResult<T>(ContentStoreOutcome.KeptAsDraft, null, workingId, [reason]);
        }

        var draft = lastRecord.Value!;
        var problems = _rules.ProblemsOf(draft);
        if (problems.Count > 0)
        {
            return new ContentStoreResult<T>(ContentStoreOutcome.KeptAsDraft, draft, workingId, problems);
        }

        var published = _tryGetLower(workingId, out _) ? Override(draft) : CreatePersistent(draft);
        _save.Writer.RemoveRecords(DraftsPath, workingId);
        return published;
    }

    /// <summary>TryPublishDraft on every draft, in the order they first appear.</summary>
    public IReadOnlyList<ContentStoreResult<T>> PublishDrafts() => [.. DraftIds().Select(TryPublishDraft)];

    /// <summary>The last line among the save's definitions holding id that isn't a removal line; null when there is none.</summary>
    /// <remarks>The file is read once, the first time this is asked; every definition line after that is kept as it is appended.</remarks>
    private string? LastDefinitionLineOf(Guid id) => LastDefinitionLines().GetValueOrDefault(id);

    private Dictionary<Guid, string> LastDefinitionLines()
    {
        if (_lastDefinitionLines is not null)
        {
            return _lastDefinitionLines;
        }

        _save.Writer.Flush();
        _lastDefinitionLines = [];
        if (File.Exists(DefinitionsPath))
        {
            foreach (var text in File.ReadAllText(DefinitionsPath, Encoding.UTF8).Split('\n').Skip(1).Select(text => text.TrimEnd('\r')))
            {
                if (ContentTable.TryReadId(text, out var lineId) && text != ContentTable.WriteRemoval(lineId))
                {
                    _lastDefinitionLines[lineId] = text;
                }
            }
        }

        return _lastDefinitionLines;
    }

    private string Header => ContentTable.Header(_rules.TableName, _types.ModuleContentVersions);

    private void AppendDefinition(T definition) => AppendDefinitionLine(_rules.IdOf(definition), ContentTable.WriteLine(definition, _types));

    private void AppendDefinitionLine(Guid id, string line)
    {
        _save.Writer.Append(DefinitionsPath, Header, line);
        if (_lastDefinitionLines is not null)
        {
            _lastDefinitionLines[id] = line;
        }
    }

    private void AppendDraft(T definition) => _save.Writer.Append(DraftsPath, Header, ContentTable.WriteLine(definition, _types));

    private IEnumerable<string> DraftLines() =>
        File.Exists(DraftsPath)
            ? File.ReadAllText(DraftsPath, Encoding.UTF8).Split('\n').Skip(1).Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)
            : [];
}

/// <summary>How a ContentStoreTable reads, names, checks and registers its definitions.</summary>
/// <param name="RecordNoun">What one record is called in a refusal ("action").</param>
/// <param name="IdNamespace">The namespace a new definition's Guid is derived under, one per table.</param>
internal sealed record ContentStoreTableRules<T>(
    string TableName,
    string FileName,
    string RecordNoun,
    Guid IdNamespace,
    Func<T, Guid> IdOf,
    Func<T, Guid, T> WithId,
    ContentStoreTableRules<T>.TryGetDefinition TryGetRegistered,
    Action<T> Register,
    Func<T, IReadOnlyList<string>> ProblemsOf)
    where T : class
{
    public delegate bool TryGetDefinition(Guid id, out T definition);
}
