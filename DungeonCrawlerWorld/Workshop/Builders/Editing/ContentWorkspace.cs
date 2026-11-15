using System.Text.Json.Nodes;
using Builders.Browsing;
using Builders.References;
using Engine.Content;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.Auras;
using Workshop;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>
/// Every table the Workshop edits, over the session it reads its catalogs from, and the one undo log they share -- so a step
/// that changes records of several tables is undone at once. It decides what may be done to a record, does it, saves the
/// tables and rebuilds the session after each save.
/// </summary>
/// <remarks>
/// <para>
/// The model alone: it says what happened, never how the status line reads it, and knows no tab. ContentEditing is the
/// side the tabs and the shell talk to.
/// </para>
/// <para>
/// What may be done to a record is decided here, once, for every caller: Delete (a master record only while nothing names
/// it), Disable (a published record), Enable (a disabled one). A refusal is a sentence saying why, which a command's
/// tooltip and the status line both show.
/// </para>
/// <para>
/// After a save the session is rebuilt on a worker over what is saved (TakeRebuiltSession), and swapped in by
/// ReplaceSession; the tabs read it through Session, References and Origins, and reopen on SessionReplaced. A save while
/// one is rebuilding starts another once it finishes, and only the last is swapped in. Documents are kept by Guid through
/// it, so selection, unsaved edits and the undo log are untouched.
/// </para>
/// <para>
/// It also answers what a record names: whether a record exists (references, for the schema check) and the definition a
/// Guid stands for (for the decode), from the records held open and then the session's content.
/// </para>
/// </remarks>
public sealed class ContentWorkspace(GameSession session, ContentSaving? saving = null) : IContentReferenceLookup, IContentReferences
{
    private readonly List<IContentEditingTable> _tables = [];
    private ContentReferenceIndex? _references;
    private ContentOrigins? _origins;
    private IReadOnlyList<ContentAppearance>? _sessionActionAppearances;
    private Task<GameSession>? _rebuild;
    private bool _isRebuildPending;

    public GameSession Session { get; private set; } = session;

    /// <summary>Changes each time a rebuilt session is swapped in.</summary>
    public int SessionVersion { get; private set; }

    public ContentTypeRegistry Types => Session.Catalogs.ContentTypes;

    /// <summary>What names what in the session's content.</summary>
    public ContentReferenceIndex References => _references ??= new ContentReferenceIndex(Session.Catalogs);

    /// <summary>Which mod supplied each record of the session.</summary>
    public ContentOrigins Origins => _origins ??= new ContentOrigins(Session.Content);

    public ContentEditLog Log { get; } = new();

    /// <summary>Whether this workspace writes files at all; a test's may not.</summary>
    public bool CanSave => saving is not null;

    /// <summary>Whether a rebuilt session is being built.</summary>
    public bool IsRebuilding => _rebuild is not null;

    /// <summary>Whether anything changed that no save has written yet, in any table.</summary>
    public bool HasUnsavedChanges => _tables.Any(table => table.HasChanges);

    /// <summary>Whether anything isn't yet in master as it is now -- unsaved, or a draft waiting to publish -- in any table: what the game wouldn't see.</summary>
    public bool HasUnpublishedChanges => _tables.Any(table => table.HasUnpublishedChanges);

    /// <summary>Raised on the main thread once a rebuilt session is swapped in (ReplaceSession).</summary>
    public event Action? SessionReplaced;

    public IReadOnlyList<IContentEditingTable> Tables => _tables;

    public void Register(IContentEditingTable table) => _tables.Add(table);

    public IContentEditingTable? TableNamed(string tableName) => _tables.FirstOrDefault(table => table.TableName == tableName);

    /// <summary>Where tableName is saved; null when this workspace saves nothing.</summary>
    public ContentTableFiles? FilesOf(string tableName) => saving?.FilesOf(tableName);

    /// <summary>Applies edit to record and records it as one step, whose origin is record.</summary>
    public void Record(IContentEditingTable table, EditableContentRecord record, ContentInspectorEdit edit)
    {
        var before = record.Json;
        edit.Apply(record);
        Log.Record(new ContentEditStep(edit.Description, table.TableName, record.Id, [new ContentEditChange(table.TableName, record.Id, before, record.Json)], edit.MergeKey, edit.FieldPath));
        if (edit.MergeKey is null)
        {
            Log.SealMerging();
        }
    }

    /// <summary>Records the making of record (New, Duplicate) as one step, whose origin it is: undone, the record goes.</summary>
    public void RecordCreation(IContentEditingTable table, EditableContentRecord record, string description)
    {
        Log.Record(new ContentEditStep(description, table.TableName, record.Id, [new ContentEditChange(table.TableName, record.Id, null, record.Json)]));
        Log.SealMerging();
    }

    /// <summary>Records the taking away of record (Delete) as one step: undone, the record is back.</summary>
    public void RecordRemoval(IContentEditingTable table, EditableContentRecord record, string description)
    {
        Log.Record(new ContentEditStep(description, table.TableName, record.Id, [new ContentEditChange(table.TableName, record.Id, record.Json, null)]));
        Log.SealMerging();
    }

    /// <summary>How every record looks, in every table held and in the session's actions while no table holds them -- what a record's look is compared with.</summary>
    public IEnumerable<ContentAppearance> AllAppearances()
    {
        foreach (var table in _tables)
        {
            foreach (var appearance in table.Appearances)
            {
                yield return appearance;
            }
        }

        if (TableNamed(GameContent.ActionsTable) is null)
        {
            foreach (var appearance in _sessionActionAppearances ??= [.. Session.Catalogs.ActionCatalog.Definitions.Select(ActivatableAppearance.Of)])
            {
                yield return appearance;
            }
        }
    }

    /// <summary>Checks every record's look against every other's again, in every table.</summary>
    public void RefreshCrossRecordProblems()
    {
        foreach (var table in _tables)
        {
            table.RefreshCrossRecordProblems();
        }
    }

    /// <summary>Everything table's id names, as the bottom panel's Uses lists it: each record its JSON names now (saved or not), then what only the session's index sees of it.</summary>
    public IReadOnlyList<WorkshopRelatedRecord> UsesOf(IContentEditingTable table, Guid id)
    {
        var uses = new List<WorkshopRelatedRecord>();
        var named = new HashSet<Guid>();
        foreach (var reference in table.ReferencesOf(id))
        {
            if (!named.Add(reference.Id))
            {
                continue;
            }

            var held = TableNamed(reference.TableName);
            var tabTitle = held?.TabTitle ?? (SessionContentTables.Named(reference.TableName) is { } sessionTable ? ContentLinks.TabTitleFor(sessionTable.Kind) : null);
            uses.Add(new WorkshopRelatedRecord(NameOf(reference.TableName, reference.Id), Singular(reference.TableName), $"at {reference.DisplayPath}")
            {
                TabTitle = tabTitle,
                RecordId = tabTitle is null ? null : reference.Id,
            });
        }

        if (SessionContentTables.Named(table.TableName) is { } tableOfSession)
        {
            uses.AddRange(ContentLinks.UsesOf(References, tableOfSession.Kind, id).Where(use => use.RecordId is not { } usedId || !named.Contains(usedId)));
        }

        return uses;
    }

    /// <summary>The name of tableName's id: a held record's as it is now, else the session's, else its Guid.</summary>
    public string NameOf(string tableName, Guid id)
    {
        if (TableNamed(tableName) is { } table && table.Contains(id))
        {
            return table.NameOf(id);
        }

        return SessionContentTables.Named(tableName)?.NameOf(Session, id) ?? ContentGuid.Format(id);
    }

    /// <summary>Everything that names tableName's id, as the bottom panel's Used By lists it.</summary>
    public IReadOnlyList<WorkshopRelatedRecord> UsedByOf(string tableName, Guid id) =>
        [.. RecordsNaming(tableName, id).Select(dependent => new WorkshopRelatedRecord(dependent.Name, dependent.Kind, dependent.Relationship)
        {
            TabTitle = dependent.Table?.TabTitle ?? dependent.TabTitle,
            RecordId = dependent.Table is not null || dependent.TabTitle is not null ? dependent.Id : null,
        })];

    /// <summary>Everything that names tableName's id: the records held open (as they are now, saved or not) and what only the session's index sees -- code (a blueprint's UsesItems) and tables no tab edits.</summary>
    public IReadOnlyList<ContentDependent> RecordsNaming(string tableName, Guid id)
    {
        var dependents = new List<ContentDependent>();
        foreach (var table in _tables)
        {
            foreach (var (fromId, name, fieldPath) in table.ReferencesTo(tableName, id))
            {
                dependents.Add(new ContentDependent(name, Singular(table.TableName), $"names it at {fieldPath}", table, fromId, null));
            }
        }

        if (SessionContentTables.Named(tableName) is { } sessionTable)
        {
            foreach (var reference in References.UsedBy(sessionTable.Kind, id))
            {
                if (SessionContentTables.Of(reference.From.Kind) is { } fromTable && TableNamed(fromTable.TableName) is { } held && (held.Contains(reference.From.Id) || held.PendingRemovals.Contains(reference.From.Id)))
                {
                    continue;
                }

                dependents.Add(new ContentDependent(reference.From.Name, reference.From.Kind.ToString(), ContentLinks.UsedByText(reference.Kind), null, reference.From.Id, ContentLinks.TabTitleFor(reference.From.Kind)));
            }
        }

        return dependents;
    }

    /// <summary>Why table's id can't be deleted now -- a mod's, already deleted, or a published record something names -- or null when it can. A draft is deleted freely.</summary>
    public string? DeleteRefusal(IContentEditingTable table, Guid id)
    {
        if (!table.Contains(id))
        {
            return table.IsDeleted(id) ? "It is already deleted." : $"No {Singular(table.TableName)} is {ContentGuid.Format(id)}.";
        }

        if (table.IsReadOnly(id))
        {
            return "A mod supplies it: delete it in the mod.";
        }

        if (!table.IsPublished(id))
        {
            return null;
        }

        var dependents = RecordsNaming(table.TableName, id);
        return dependents.Count == 0 ? null : $"{DependentsText(dependents)} names it -- see Used By.";
    }

    /// <summary>Why table's id can't be disabled now -- a mod's, a draft (deleted instead), or disabled already -- or null when it can.</summary>
    public string? DisableRefusal(IContentEditingTable table, Guid id)
    {
        if (!table.Contains(id))
        {
            return table.IsDeleted(id) ? "A deleted record can't be disabled; restore it first." : $"No {Singular(table.TableName)} is {ContentGuid.Format(id)}.";
        }

        if (table.IsReadOnly(id))
        {
            return "A mod supplies it: disable it in the mod.";
        }

        if (!table.IsPublished(id))
        {
            return $"Only a published {Singular(table.TableName)} can be disabled; a draft is deleted instead.";
        }

        return table.IsDisabled(id) ? "It is already disabled." : null;
    }

    /// <summary>Why table's id can't be enabled now -- it isn't disabled -- or null when it can.</summary>
    public string? EnableRefusal(IContentEditingTable table, Guid id) =>
        table.Contains(id) && table.IsDisabled(id) && !table.IsReadOnly(id) ? null : "It isn't disabled.";

    /// <summary>Takes table's id away as one step; returns why it couldn't (DeleteRefusal), or null once it is deleted.</summary>
    public string? Delete(IContentEditingTable table, Guid id)
    {
        if (DeleteRefusal(table, id) is { } refusal)
        {
            return refusal;
        }

        table.Delete(id, $"Delete {table.NameOf(id)}");
        return null;
    }

    /// <summary>What disabling table's id touches: each record that names it, and for Cascade every published record that would be disabled with it, in turn -- drafts and what can't be edited here listed apart.</summary>
    public ContentDisablePlan PlanDisable(IContentEditingTable table, Guid id)
    {
        var direct = RecordsNaming(table.TableName, id).Where(dependent => dependent.Table is not { } held || !held.IsDisabled(dependent.Id)).ToList();
        var cascaded = new List<ContentDependent>();
        var notEditable = new List<ContentDependent>();
        var drafts = new List<ContentDependent>();
        var visited = new HashSet<ContentRecordKey> { new(table.TableName, id) };
        var queue = new Queue<ContentDependent>(direct);
        while (queue.TryDequeue(out var dependent))
        {
            if (dependent.Table is not { } held)
            {
                notEditable.Add(dependent);
                continue;
            }

            if (!visited.Add(new ContentRecordKey(held.TableName, dependent.Id)) || held.IsDisabled(dependent.Id))
            {
                continue;
            }

            if (!held.IsPublished(dependent.Id))
            {
                drafts.Add(dependent);
                continue;
            }

            cascaded.Add(dependent);
            foreach (var next in RecordsNaming(held.TableName, dependent.Id))
            {
                queue.Enqueue(next);
            }
        }

        return new ContentDisablePlan(direct, cascaded, notEditable, drafts);
    }

    /// <summary>Disables table's id, replaced by replacement or by nothing, as one step; returns why it couldn't (DisableRefusal), or null once it is disabled.</summary>
    public string? Disable(IContentEditingTable table, Guid id, Guid? replacement)
    {
        if (DisableRefusal(table, id) is { } refusal)
        {
            return refusal;
        }

        ApplyStep($"Disable {table.NameOf(id)}", table, id, [(table, id, record => SetDisabled(record, replacement))], FieldPathOf(replacement is null ? ContentTable.DisabledKey : ContentTable.ReplacementKey));
        return null;
    }

    /// <summary>Disables table's id and every published record its plan cascades to, as one step; returns why it couldn't -- the record's own refusal, or anything that can't be edited here naming one of them -- or null once they are disabled.</summary>
    public string? DisableWithCascade(IContentEditingTable table, Guid id, out int cascadedCount)
    {
        cascadedCount = 0;
        if (DisableRefusal(table, id) is { } refusal)
        {
            return refusal;
        }

        var plan = PlanDisable(table, id);
        if (plan.CascadeRefusal is { } cascadeRefusal)
        {
            return cascadeRefusal;
        }

        ApplyStep($"Disable {table.NameOf(id)} and what names it", table, id,
            [(table, id, record => SetDisabled(record, null)), .. plan.Cascaded.Select(dependent => (dependent.Table!, dependent.Id, (Action<EditableContentRecord>)(record => SetDisabled(record, null))))],
            FieldPathOf(ContentTable.DisabledKey));
        cascadedCount = plan.Cascaded.Count;
        return null;
    }

    /// <summary>Enables table's id -- its "off" and "rep" taken away -- as one step; returns why it couldn't (EnableRefusal), or null once it is enabled.</summary>
    public string? Enable(IContentEditingTable table, Guid id)
    {
        if (EnableRefusal(table, id) is { } refusal)
        {
            return refusal;
        }

        ApplyStep($"Enable {table.NameOf(id)}", table, id, [(table, id, record =>
        {
            record.RemoveField(ContentPath.Root, ContentTable.DisabledKey);
            record.RemoveField(ContentPath.Root, ContentTable.ReplacementKey);
        })], FieldPathOf(ContentTable.DisabledKey));
        return null;
    }

    /// <summary>The path of a key at the top of a record, as a step names the field it changed.</summary>
    private static string FieldPathOf(string key) => ContentPath.Root.Key(key).Text;

    private void SetDisabled(EditableContentRecord record, Guid? replacement)
    {
        var schema = Types.GetValueCodec(record.ValueType).Schema;
        record.SetField(ContentPath.Root, schema, ContentTable.DisabledKey, JsonValue.Create(true));
        if (replacement is { } replacementId)
        {
            record.SetField(ContentPath.Root, schema, ContentTable.ReplacementKey, JsonValue.Create(ContentGuid.Format(replacementId)));
        }
        else
        {
            record.RemoveField(ContentPath.Root, ContentTable.ReplacementKey);
        }
    }

    /// <summary>Applies each change to its record, in order, as one step of the log whose origin is originTable's originId.</summary>
    private void ApplyStep(string description, IContentEditingTable originTable, Guid originId, IReadOnlyList<(IContentEditingTable Table, Guid Id, Action<EditableContentRecord> Change)> changes, string originFieldPath)
    {
        var recorded = new List<ContentEditChange>();
        foreach (var (table, id, change) in changes)
        {
            var before = table.JsonOf(id);
            table.Change(id, change);
            recorded.Add(new ContentEditChange(table.TableName, id, before, table.JsonOf(id)));
        }

        Log.Record(new ContentEditStep(description, originTable.TableName, originId, recorded, OriginFieldPath: originFieldPath));
        Log.SealMerging();
    }

    /// <summary>The name of tableName's id when it is held, disabled, and its replacements end in nothing (or come back round); null otherwise.</summary>
    public string? ResolvesToNothing(string tableName, Guid id)
    {
        if (TableNamed(tableName) is not { } table || !table.IsDisabled(id))
        {
            return null;
        }

        var visited = new HashSet<Guid>();
        for (Guid? current = id; current is { } currentId; current = table.ReplacementOf(currentId))
        {
            if (!table.Contains(currentId) || !table.IsDisabled(currentId))
            {
                return null;
            }

            if (!visited.Add(currentId))
            {
                break;
            }
        }

        return table.NameOf(id);
    }

    /// <summary>Why replacement can't replace table's id: it isn't a published record of the table, or following replacements from it comes back to id; null when it can.</summary>
    public string? ReplacementProblem(IContentEditingTable table, Guid id, Guid replacement)
    {
        if (!table.Contains(replacement))
        {
            return $"Replaced by {ContentGuid.Format(replacement)}, which no {Singular(table.TableName)} is.";
        }

        if (!table.IsPublished(replacement))
        {
            return $"Replaced by {table.NameOf(replacement)}, which isn't published.";
        }

        var chain = new List<string> { table.NameOf(id) };
        var visited = new HashSet<Guid> { id };
        for (Guid? current = replacement; current is { } currentId; current = table.IsDisabled(currentId) ? table.ReplacementOf(currentId) : null)
        {
            chain.Add(table.NameOf(currentId));
            if (!visited.Add(currentId))
            {
                return $"Replacements come back round: {string.Join(" -> ", chain)}.";
            }
        }

        return null;
    }

    /// <summary>Saves every record that changed to Drafts (Save), or tries to publish it (Publish) -- or only the records named -- and starts a rebuild when anything was written; an empty report when this workspace saves nothing.</summary>
    public ContentSaveReport Save(IReadOnlySet<ContentRecordKey>? only, bool publishes)
    {
        if (saving is null)
        {
            return ContentSaveReport.Failed("Nothing is saved here.");
        }

        var report = new ContentSaver(saving, _tables).Save(only, publishes);
        if (report.Results.Count > 0 || report.RemovedCount > 0 || report.RefusedRemovals.Count > 0)
        {
            RequestRebuild();
        }

        return report;
    }

    /// <summary>Starts building a session over what is saved, on a worker; while one builds, another is started once it finishes.</summary>
    public void RequestRebuild()
    {
        if (saving is null)
        {
            return;
        }

        if (_rebuild is not null)
        {
            _isRebuildPending = true;
            return;
        }

        _rebuild = Task.Run(saving.BuildSession);
    }

    /// <summary>The rebuilt session once the latest rebuild is built -- on the main thread, between frames; null while none has finished, and failure set when it threw. Swap it in with ReplaceSession.</summary>
    public GameSession? TakeRebuiltSession(out string? failure)
    {
        failure = null;
        if (_rebuild is not { IsCompleted: true } rebuild)
        {
            return null;
        }

        _rebuild = null;
        if (_isRebuildPending)
        {
            _isRebuildPending = false;
            RequestRebuild();
            return null;
        }

        if (rebuild.IsFaulted)
        {
            failure = rebuild.Exception?.GetBaseException().Message;
            return null;
        }

        return rebuild.Result;
    }

    /// <summary>Makes rebuilt the session every table is checked against and every tab reads, then raises SessionReplaced.</summary>
    public void ReplaceSession(GameSession rebuilt)
    {
        Session = rebuilt;
        _references = null;
        _origins = null;
        _sessionActionAppearances = null;
        SessionVersion++;
        foreach (var table in _tables)
        {
            table.EvaluateAll();
        }

        SessionReplaced?.Invoke();
    }

    /// <summary>Undoes the latest step -- each record it changed put back -- and returns it; null when there is none.</summary>
    public ContentEditStep? Undo()
    {
        if (Log.Undo() is not { } step)
        {
            return null;
        }

        foreach (var change in step.Changes)
        {
            TableNamed(change.TableName)?.Restore(change.RecordId, change.Before);
        }

        return step;
    }

    /// <summary>Redoes the step undone last and returns it; null when there is none.</summary>
    public ContentEditStep? Redo()
    {
        if (Log.Redo() is not { } step)
        {
            return null;
        }

        foreach (var change in step.Changes)
        {
            TableNamed(change.TableName)?.Restore(change.RecordId, change.After);
        }

        return step;
    }

    /// <summary>"item" for Items: one record of tableName, lower case.</summary>
    public static string Singular(string tableName) =>
        (tableName.EndsWith('s') ? tableName[..^1] : tableName).ToLowerInvariant();

    private static string DependentsText(IReadOnlyList<ContentDependent> dependents) =>
        dependents.Count <= 2 ? string.Join(" and ", dependents.Select(dependent => $"{dependent.Kind} {dependent.Name}")) : $"{dependents[0].Kind} {dependents[0].Name} and {dependents.Count - 1} more";

    /// <summary>Whether tableName holds id: a record held open, else one the session has; false for a table no record can name.</summary>
    public bool Exists(string tableName, Guid id)
    {
        if (TableNamed(tableName) is { } table)
        {
            return table.Contains(id);
        }

        return SessionContentTables.Named(tableName) is { } sessionTable && sessionTable.NameOf(Session, id) is not null;
    }

    /// <summary>The definition id stands for: a held record's as it last decoded, else (an aura) the session's content.</summary>
    public bool TryResolve<T>(Guid id, out T value)
        where T : class
    {
        foreach (var table in _tables)
        {
            if (table.ValueType == typeof(T) && table.DefinitionOf(id) is T held)
            {
                value = held;
                return true;
            }
        }

        if (typeof(T) == typeof(AuraDefinition) && Session.Content.TryLoadAura(id, out var aura))
        {
            value = (T)(object)aura;
            return true;
        }

        value = null!;
        return false;
    }
}
