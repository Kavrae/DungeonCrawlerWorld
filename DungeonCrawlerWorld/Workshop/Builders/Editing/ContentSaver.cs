using Engine.Content;
using Game.Content;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>Saves the workspace's tables: where each record and deletion goes, trying a publish in a build of master first, and the writes of every file.</summary>
/// <remarks>
/// <para>
/// Save writes every changed record to Drafts and never touches master. Publish sends each record where it belongs: one
/// with an error at any layer, or naming a record that is only a draft goes to Drafts; the rest are tried in a build of
/// master alone with their lines in place, each the build fails goes to Drafts with its failure and the trial runs again
/// without it, and what is left is written to master (its draft, if any, removed). A failure of the whole build, or of a
/// record that is no candidate where master as saved builds it, sends every remaining record to Drafts and keeps every
/// deletion of a master record there too. Only the lines of the records saved change.
/// </para>
/// <para>
/// Master is read once per save: the trial builds and the writes start from that one reading, so what was tried is what
/// is written. Every table's files are staged before any is replaced, so a file the disk refuses changes nothing; a table
/// whose files can't be moved into place stays unsaved, and the report says which.
/// </para>
/// </remarks>
internal sealed class ContentSaver(ContentSaving saving, IReadOnlyList<IContentEditingTable> tables)
{
    /// <summary>Writes the records and deletions a save takes -- every one, or only those named -- to Drafts, or, when publishes, each that passes to master.</summary>
    public ContentSaveReport Save(IReadOnlySet<ContentRecordKey>? only, bool publishes)
    {
        var removalReasons = new Dictionary<ContentRecordKey, IReadOnlyList<string>>();
        var saves = new List<TableSave>();
        try
        {
            saves.AddRange(Plan(only, publishes, removalReasons));
            foreach (var save in saves)
            {
                save.Writes.Stage();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            foreach (var save in saves)
            {
                save.Writes.Discard();
            }

            return ContentSaveReport.Failed($"Couldn't save, so nothing was written: {exception.Message}");
        }

        var saved = new List<ContentSaveResult>();
        var removedCount = 0;
        string? writeFailure = null;
        foreach (var save in saves)
        {
            if (writeFailure is not null)
            {
                save.Writes.Discard();
                continue;
            }

            try
            {
                save.Writes.Commit();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                writeFailure = $"Couldn't save {save.Table.TableName}, which stays unsaved: {exception.Message}";
                continue;
            }

            var tableName = save.Table.TableName;
            var tableReasons = removalReasons.Where(reason => reason.Key.TableName == tableName).ToDictionary(reason => reason.Key.Id, reason => reason.Value);
            save.Table.CompleteSave(save.Results, save.Removals, save.RemovalLines, tableReasons);
            saved.AddRange(save.Results);
            removedCount += save.Removals.Count + save.RemovalLines.Count(id => !tableReasons.ContainsKey(id));
        }

        return new ContentSaveReport(saved, removedCount, removalReasons, writeFailure);
    }

    /// <summary>Where each record and deletion a save takes goes -- tried in a build of master first when publishes -- as each table's part, with the writes of its files; removalReasons gets why each deletion a publish refused was kept in Drafts.</summary>
    private List<TableSave> Plan(IReadOnlySet<ContentRecordKey>? only, bool publishes, Dictionary<ContentRecordKey, IReadOnlyList<string>> removalReasons)
    {
        var master = ReadMaster(publishes);
        var entries = tables.SelectMany(table => table.SaveEntries(IdsOf(table, only), publishes)).ToList();
        var removals = tables.ToDictionary(table => table.TableName, table => Only(table, publishes ? table.PendingRemovals : table.PendingDraftRemovals, only));
        var removalLines = tables.ToDictionary(table => table.TableName, table => publishes ? [] : Only(table, table.PendingRemovalLines, only));
        var reasons = entries.ToDictionary(KeyOf, _ => new List<string>());
        var candidates = publishes ? entries.Where(entry => !entry.HasErrors && entry.PublishedLine is not null).ToList() : [];
        var movedMaster = new Dictionary<ContentRecordKey, string?>();

        if (publishes)
        {
            RefuseMovedMaster(master, candidates, reasons, movedMaster);
            RefuseDraftReferences(candidates, removals, reasons);
            if (RefuseTrialFailures(master, candidates, removals, reasons) is { } refusal)
            {
                foreach (var candidate in candidates)
                {
                    reasons[KeyOf(candidate)].Add(refusal);
                }

                candidates.Clear();
                KeepMasterRemovalsAsDrafts(master, removals, removalLines, removalReasons, refusal);
            }
        }

        var published = candidates.Select(KeyOf).ToHashSet();
        var results = entries.Select(entry => published.Contains(KeyOf(entry))
            ? new ContentSaveResult(entry.TableName, entry.Id, ContentSaveDestination.Master, [], entry.PublishedLine)
            : new ContentSaveResult(entry.TableName, entry.Id, ContentSaveDestination.Drafts, reasons[KeyOf(entry)], null)
            {
                IsMasterMoved = movedMaster.ContainsKey(KeyOf(entry)),
                MasterLineNow = movedMaster.GetValueOrDefault(KeyOf(entry)),
            }).ToList();
        var entriesByKey = entries.ToDictionary(KeyOf);

        var saves = new List<TableSave>();
        foreach (var table in tables)
        {
            var tableResults = results.Where(result => result.TableName == table.TableName).ToList();
            var tableRemovals = removals[table.TableName];
            var tableRemovalLines = removalLines[table.TableName];
            if (tableResults.Count == 0 && tableRemovals.Count == 0 && tableRemovalLines.Count == 0)
            {
                continue;
            }

            saves.Add(new TableSave(table, tableResults, tableRemovals, tableRemovalLines, PlanWrites(master.Tables[table.TableName], tableResults, entriesByKey, tableRemovals, tableRemovalLines)));
        }

        return saves;
    }

    private static ContentRecordKey KeyOf(ContentSaveEntry entry) => new(entry.TableName, entry.Id);

    private static IReadOnlySet<Guid>? IdsOf(IContentEditingTable table, IReadOnlySet<ContentRecordKey>? only) =>
        only is null ? null : only.Where(key => key.TableName == table.TableName).Select(key => key.Id).ToHashSet();

    private static List<Guid> Only(IContentEditingTable table, IReadOnlyList<Guid> ids, IReadOnlySet<ContentRecordKey>? only) =>
        [.. only is null ? ids : ids.Where(id => only.Contains(new ContentRecordKey(table.TableName, id)))];

    /// <summary>Master as it is now, read once: every file when publishing (what the trial builds are given), each edited table's lines either way.</summary>
    private MasterReading ReadMaster(bool publishes)
    {
        var files = publishes ? saving.ReadMasterFiles() : null;
        var tableLines = new Dictionary<string, (ContentTableFiles, ContentTableLines)>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            var tableFiles = saving.FilesOf(table.TableName);
            tableLines[table.TableName] = (tableFiles, files is null ? tableFiles.ReadMaster() : tableFiles.MasterFrom(files));
        }

        return new MasterReading(files ?? new Dictionary<string, string>(), tableLines);
    }

    /// <summary>Why a publish won't write over a master line that changed under the Workshop.</summary>
    public const string MasterMovedReason = "Master changed on disk since this was opened, so it wasn't written over. Reload Master to compare with master as it is now, then publish again.";

    /// <summary>
    /// Sends to Drafts each candidate whose master line on disk isn't the one its edits were made against -- changed,
    /// added or taken away by something else (git, another editor) since it was opened -- putting the line found in
    /// movedMaster, so the record can take it as its baseline.
    /// </summary>
    private static void RefuseMovedMaster(MasterReading master, List<ContentSaveEntry> candidates, Dictionary<ContentRecordKey, List<string>> reasons, Dictionary<ContentRecordKey, string?> movedMaster)
    {
        foreach (var candidate in candidates.ToList())
        {
            var (_, lines) = master.Tables[candidate.TableName];
            var lineNow = lines.TryGetLine(candidate.Id, out var line) ? line : null;
            if (lineNow == candidate.OpenedMasterLine)
            {
                continue;
            }

            reasons[KeyOf(candidate)].Add(MasterMovedReason);
            movedMaster[KeyOf(candidate)] = lineNow;
            candidates.Remove(candidate);
        }
    }

    /// <summary>Sends to Drafts each candidate naming a record held open that, after this save, would be only a draft -- until none is left that does.</summary>
    private void RefuseDraftReferences(List<ContentSaveEntry> candidates, Dictionary<string, List<Guid>> removals, Dictionary<ContentRecordKey, List<string>> reasons)
    {
        for (var changed = true; changed;)
        {
            changed = false;
            var publishing = candidates.Select(KeyOf).ToHashSet();
            foreach (var candidate in candidates.ToList())
            {
                foreach (var reference in candidate.References)
                {
                    if (TableNamed(reference.TableName) is not { } table || !table.Contains(reference.Id) || publishing.Contains(new ContentRecordKey(reference.TableName, reference.Id)))
                    {
                        continue;
                    }

                    if (table.IsPublished(reference.Id) && !removals[reference.TableName].Contains(reference.Id))
                    {
                        continue;
                    }

                    reasons[KeyOf(candidate)].Add($"{reference.DisplayPath} names {table.NameOf(reference.Id)}, which is only a draft.");
                    candidates.Remove(candidate);
                    changed = true;
                    break;
                }
            }
        }
    }

    private IContentEditingTable? TableNamed(string tableName) => tables.FirstOrDefault(table => table.TableName == tableName);

    /// <summary>
    /// Tries the candidates and removals in a build of master alone: each candidate the build fails goes to Drafts with
    /// its failure, and the trial runs again without it. Returns why nothing at all may be published -- the build failing
    /// as a whole, or failing a record that is no candidate where master as saved doesn't -- else null.
    /// </summary>
    private string? RefuseTrialFailures(MasterReading master, List<ContentSaveEntry> candidates, Dictionary<string, List<Guid>> removals, Dictionary<ContentRecordKey, List<string>> reasons)
    {
        HashSet<(string File, Guid? RecordId, string Reason)>? failingAsSaved = null;
        while (candidates.Count > 0 || removals.Values.Any(ids => ids.Count > 0))
        {
            var trial = saving.TrialBuild(master.FilesWith(candidates, removals));
            if (trial.BuildFailure is { } buildFailure)
            {
                return $"Master doesn't build with it: {buildFailure}";
            }

            var failed = candidates.Where(candidate => trial.Failures.Any(failure => IsFailureOf(failure, candidate))).ToList();
            if (failed.Count > 0)
            {
                foreach (var candidate in failed)
                {
                    foreach (var failure in trial.Failures.Where(failure => IsFailureOf(failure, candidate)))
                    {
                        reasons[KeyOf(candidate)].Add($"Master doesn't build with it: {failure.Reason}");
                    }

                    candidates.Remove(candidate);
                }

                continue;
            }

            if (trial.Failures.Count == 0)
            {
                return null;
            }

            failingAsSaved ??= FailuresOfMasterAsSaved(master);
            var caused = trial.Failures.Where(failure => !failingAsSaved.Contains(FailureKeyOf(failure))).ToList();
            return caused.Count == 0
                ? null
                : $"Master doesn't build with it: {caused[0].Location}: {caused[0].Reason}{(caused.Count > 1 ? $" (and {caused.Count - 1} more)" : string.Empty)}";
        }

        return null;
    }

    private static bool IsFailureOf(ContentFailure failure, ContentSaveEntry candidate) =>
        failure.RecordId == candidate.Id && GameContent.IsFileOf(candidate.TableName, failure.File);

    private static (string File, Guid? RecordId, string Reason) FailureKeyOf(ContentFailure failure) => (failure.File, failure.RecordId, failure.Reason);

    /// <summary>What a trial of master as saved fails, by file, record and reason (lines move with a publish); none when it doesn't build at all, so every failure a publish's trial finds is taken as its own.</summary>
    private HashSet<(string File, Guid? RecordId, string Reason)> FailuresOfMasterAsSaved(MasterReading master)
    {
        var trial = saving.TrialBuild(master.Files);
        return trial.BuildFailure is null ? [.. trial.Failures.Select(FailureKeyOf)] : [];
    }

    /// <summary>Takes out of removals each deletion of a record master holds, and records it in Drafts instead, with why.</summary>
    private static void KeepMasterRemovalsAsDrafts(MasterReading master, Dictionary<string, List<Guid>> removals, Dictionary<string, List<Guid>> removalLines, Dictionary<ContentRecordKey, IReadOnlyList<string>> removalReasons, string refusal)
    {
        foreach (var (tableName, ids) in removals)
        {
            var (_, lines) = master.Tables[tableName];
            foreach (var id in ids.Where(lines.Contains).ToList())
            {
                ids.Remove(id);
                removalLines[tableName].Add(id);
                removalReasons[new ContentRecordKey(tableName, id)] = [refusal];
            }
        }
    }

    /// <summary>
    /// The writes of one table's master and drafts files, from master as read: each published record's line put in master
    /// and its draft removed, each drafted record's line put in Drafts (or its draft removed, when it is master's record as
    /// it stands), each removal taken out of both, each of removalLines recorded in Drafts as deleted -- every other line
    /// kept, and a file that wouldn't change left alone.
    /// </summary>
    private static ContentFileWrites PlanWrites((ContentTableFiles Files, ContentTableLines Lines) masterTable, IReadOnlyList<ContentSaveResult> results, Dictionary<ContentRecordKey, ContentSaveEntry> entries, IReadOnlyList<Guid> removals, IReadOnlyList<Guid> removalLines)
    {
        var files = masterTable.Files;
        var masterText = masterTable.Lines.ToText();
        var master = ContentTableLines.Parse(masterText);
        var drafts = files.ReadDrafts();
        var draftsText = drafts.ToText();
        foreach (var result in results)
        {
            if (result.Destination == ContentSaveDestination.Master)
            {
                master.Put(result.Id, result.PublishedLine!);
                drafts.Remove(result.Id);
                continue;
            }

            var entry = entries[new ContentRecordKey(result.TableName, result.Id)];
            if (master.TryGetLine(result.Id, out var masterLine) && ContentTableFiles.IsSameRecord(masterLine, entry.DraftJson))
            {
                drafts.Remove(result.Id);
                continue;
            }

            drafts.Put(result.Id, entry.DraftJson);
        }

        foreach (var id in removals)
        {
            master.Remove(id);
            drafts.Remove(id);
        }

        foreach (var id in removalLines)
        {
            drafts.Put(id, ContentTableFiles.RemovalLine(id));
        }

        var writes = new ContentFileWrites();
        if (master.ToText() != masterText)
        {
            files.WriteMaster(writes, master);
        }

        if (drafts.ToText() != draftsText)
        {
            files.WriteDrafts(writes, drafts);
        }

        return writes;
    }

    /// <summary>One table's part of a save: where its records went, its removals, and the writes of its files.</summary>
    private sealed record TableSave(IContentEditingTable Table, IReadOnlyList<ContentSaveResult> Results, IReadOnlyList<Guid> Removals, IReadOnlyList<Guid> RemovalLines, ContentFileWrites Writes);

    /// <summary>Master as one save read it: every file (when publishing), and each edited table's files and lines.</summary>
    private sealed record MasterReading(IReadOnlyDictionary<string, string> Files, IReadOnlyDictionary<string, (ContentTableFiles Files, ContentTableLines Lines)> Tables)
    {
        /// <summary>Every master file, each edited table's with the candidates' lines in place and the removals taken out.</summary>
        public Dictionary<string, string> FilesWith(IReadOnlyList<ContentSaveEntry> candidates, Dictionary<string, List<Guid>> removals)
        {
            var files = new Dictionary<string, string>(Files, StringComparer.Ordinal);
            foreach (var (tableName, (tableFiles, saved)) in Tables)
            {
                var lines = ContentTableLines.Parse(saved.ToText());
                foreach (var candidate in candidates.Where(candidate => candidate.TableName == tableName))
                {
                    lines.Put(candidate.Id, candidate.PublishedLine!);
                }

                foreach (var id in removals[tableName])
                {
                    lines.Remove(id);
                }

                files[tableFiles.FileName] = lines.ToText();
            }

            return files;
        }
    }
}
