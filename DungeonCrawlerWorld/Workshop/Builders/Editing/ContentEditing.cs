using Game.Bootstrap;
using Workshop;
using Workshop.Editing;

namespace Builders.Editing;

/// <summary>What a tab shows of its table: whether it is open, and selecting a record in it.</summary>
public interface IContentTableView
{
    string TableName { get; }

    bool IsOpen { get; }

    void ShowRecord(Guid id, bool rebuildInspector);

    /// <summary>Takes the player to fieldPath of the record shown: opens what hides it and focuses its control.</summary>
    void FocusField(string fieldPath);
}

/// <summary>
/// The side of the workspace the tabs and the shell talk to: every command on records, saving, publishing, undo and redo,
/// each answered with what the status line says, and the session swapped in between frames.
/// </summary>
/// <remarks>
/// <para>
/// The workspace decides and does; this says what happened, puts back or commits text typed into the open tab's inspector
/// first (the field text the open tab attaches), and takes the player to what undo or redo changed: a tab already open
/// that shows a record of the step stays, with that record selected (the step's origin when it shows it); otherwise the
/// origin's tab opens on it.
/// </para>
/// <para>
/// A rebuilt session is swapped in in one order, here: first replaceSessionServices (the element factories whose windows
/// read the session), then the workspace's session, whose SessionReplaced reopens the tabs -- so no tab reopens onto a
/// window built over the old session.
/// </para>
/// </remarks>
/// <param name="replaceSessionServices">Rebinds what reads the session outside the tabs (window factories) to a rebuilt one, before any tab reopens.</param>
public sealed class ContentEditing(ContentWorkspace workspace, Action<GameSession>? replaceSessionServices = null)
{
    private readonly Dictionary<string, IContentTableView> _views = new(StringComparer.Ordinal);
    private IUncommittedFieldText? _fieldText;

    public ContentWorkspace Workspace { get; } = workspace;

    /// <summary>Shows its table's records: the tab that edits it.</summary>
    public void RegisterView(IContentTableView view) => _views[view.TableName] = view;

    /// <summary>The open tab's inspector: what undo puts back and a save commits first, until DetachFieldText.</summary>
    public void AttachFieldText(IUncommittedFieldText fieldText) => _fieldText = fieldText;

    public void DetachFieldText(IUncommittedFieldText fieldText)
    {
        if (ReferenceEquals(_fieldText, fieldText))
        {
            _fieldText = null;
        }
    }

    /// <summary>Saves every record that changed to Drafts, master untouched, and records each deletion there (Ctrl+S).</summary>
    public string Save()
    {
        _fieldText?.CommitUncommitted();
        if (!Workspace.CanSave)
        {
            return "Nothing is saved here.";
        }

        var report = Workspace.Save(only: null, publishes: false);
        return report.IsEmpty ? "Nothing to save." : Describe(report);
    }

    /// <summary>Tries to publish every record that changed and every draft, and every deletion (Ctrl+Shift+S): what fails or names a draft is saved to Drafts instead.</summary>
    public string PublishAll()
    {
        _fieldText?.CommitUncommitted();
        if (!Workspace.CanSave)
        {
            return "Nothing is saved here.";
        }

        var report = Workspace.Save(only: null, publishes: true);
        return report.IsEmpty ? "Nothing to publish." : Describe(report);
    }

    /// <summary>Tries to publish id alone -- its deletion, when it is deleted; when anything keeps it out of master, it is saved to Drafts instead, with why.</summary>
    public string Publish(IContentEditingTable table, Guid id)
    {
        _fieldText?.CommitUncommitted();
        if (!Workspace.CanSave)
        {
            return "Nothing is saved here.";
        }

        var name = table.NameOf(id);
        var isDeletion = table.IsDeleted(id);
        var key = new ContentRecordKey(table.TableName, id);
        var report = Workspace.Save(new HashSet<ContentRecordKey> { key }, publishes: true);
        if (report.WriteFailure is { } failure)
        {
            return failure;
        }

        if (isDeletion)
        {
            return !table.IsDeleted(id)
                ? $"Deleted {name} for good."
                : $"Not deleted from master, the deletion saved as a draft: {string.Join(" ", report.RefusedRemovals.GetValueOrDefault(key) ?? [])}";
        }

        return report.Results.FirstOrDefault(result => result.TableName == key.TableName && result.Id == key.Id) is { Destination: ContentSaveDestination.Drafts } drafted
            ? $"Not published, saved as a draft: {(drafted.Reasons.Count > 0 ? string.Join(" ", drafted.Reasons) : "it has errors -- see the Error List.")}"
            : $"Published {name}.";
    }

    /// <summary>Saves id alone to Drafts -- its deletion, when it is deleted; a master record keeps its published line.</summary>
    public string SaveAsDraft(IContentEditingTable table, Guid id)
    {
        _fieldText?.CommitUncommitted();
        if (!Workspace.CanSave)
        {
            return "Nothing is saved here.";
        }

        var name = table.NameOf(id);
        var isDeletion = table.IsDeleted(id);
        var report = Workspace.Save(new HashSet<ContentRecordKey> { new(table.TableName, id) }, publishes: false);
        if (report.WriteFailure is { } failure)
        {
            return failure;
        }

        if (isDeletion)
        {
            return table.IsDeleted(id) ? $"Saved the deletion of {name} as a draft." : $"Deleted {name} for good.";
        }

        return $"Saved {name} as a draft.";
    }

    /// <summary>Deletes id, when the workspace allows it.</summary>
    public string Delete(IContentEditingTable table, Guid id)
    {
        var name = table.NameOf(id);
        var isPublished = table.IsPublished(id);
        if (Workspace.Delete(table, id) is { } refusal)
        {
            return $"Can't delete {name}: {refusal}";
        }

        return isPublished ? $"Deleted {name}: publishing takes it out of master."
            : table.IsDeleted(id) ? $"Deleted {name}: saving takes it out of Drafts."
            : $"Deleted {name}.";
    }

    /// <summary>Disables id, replaced by replacement or by nothing, when the workspace allows it.</summary>
    public string Disable(IContentEditingTable table, Guid id, Guid? replacement)
    {
        var name = table.NameOf(id);
        if (Workspace.Disable(table, id, replacement) is { } refusal)
        {
            return $"Not disabled: {refusal}";
        }

        return replacement is { } replacementId ? $"Disabled {name}, replaced by {table.NameOf(replacementId)}." : $"Disabled {name}.";
    }

    /// <summary>Disables id and every published record that names it, in turn, when the workspace allows it.</summary>
    public string DisableWithCascade(IContentEditingTable table, Guid id)
    {
        var name = table.NameOf(id);
        return Workspace.DisableWithCascade(table, id, out var cascadedCount) is { } refusal
            ? $"Not disabled: {refusal}"
            : $"Disabled {name} and {cascadedCount} more.";
    }

    public string Enable(IContentEditingTable table, Guid id)
    {
        var name = table.NameOf(id);
        return Workspace.Enable(table, id) is { } refusal ? $"Not enabled: {refusal}" : $"Enabled {name}.";
    }

    /// <summary>Puts back typed text if there is any, else undoes the latest step and takes the player to it -- through navigator, when another tab must open.</summary>
    public string Undo(TabNavigator? navigator)
    {
        if (_fieldText?.RevertUncommitted() == true)
        {
            return "Put back what was typed.";
        }

        if (Workspace.Undo() is not { } step)
        {
            return "Nothing to undo.";
        }

        TakeThePlayerTo(step, navigator);
        return $"Undid {step.Description}: {Count(step)}.";
    }

    /// <summary>Redoes the step undone last and takes the player to it.</summary>
    public string Redo(TabNavigator? navigator)
    {
        if (Workspace.Redo() is not { } step)
        {
            return "Nothing to redo.";
        }

        TakeThePlayerTo(step, navigator);
        return $"Redid {step.Description}: {Count(step)}.";
    }

    /// <summary>Swaps in a rebuilt session once it is built -- between frames, on the main thread; returns a status line when something finished.</summary>
    public string? Update()
    {
        if (Workspace.TakeRebuiltSession(out var failure) is not { } rebuilt)
        {
            return failure is null ? null : $"Rebuilding failed: {failure}";
        }

        replaceSessionServices?.Invoke(rebuilt);
        Workspace.ReplaceSession(rebuilt);
        return "Rebuilt.";
    }

    /// <summary>The open tab when it shows a record of the step (the origin if it is one of them), else the origin's tab -- always a record the step left in place, never one it took away (a New undone) -- with the control of the field the step changed focused, when the record shown is its origin.</summary>
    private void TakeThePlayerTo(ContentEditStep step, TabNavigator? navigator)
    {
        foreach (var change in step.Changes)
        {
            if (_views.GetValueOrDefault(change.TableName) is { IsOpen: true } openView && Workspace.TableNamed(change.TableName) is { } openTable && ShownRecordOf(step, openTable) is { } shown)
            {
                openView.ShowRecord(shown, rebuildInspector: true);
                if (FieldPathShownFor(step, openTable, shown) is { } fieldPath)
                {
                    openView.FocusField(fieldPath);
                }

                return;
            }
        }

        if (Workspace.TableNamed(step.OriginTableName) is { } originTable && ShownRecordOf(step, originTable) is { } origin)
        {
            navigator?.RequestOpenRecord(originTable.TabTitle, origin, FieldPathShownFor(step, originTable, origin));
        }
    }

    /// <summary>The field the step changed, when the record shown is the step's origin; null otherwise (another record of the step has no field of its own named).</summary>
    private static string? FieldPathShownFor(ContentEditStep step, IContentEditingTable table, Guid shown) =>
        step.OriginTableName == table.TableName && step.OriginRecordId == shown ? step.OriginFieldPath : null;

    /// <summary>The step's origin when table holds it, else the first of the step's records table holds; null for none.</summary>
    private static Guid? ShownRecordOf(ContentEditStep step, IContentEditingTable table)
    {
        if (step.OriginTableName == table.TableName && table.Contains(step.OriginRecordId))
        {
            return step.OriginRecordId;
        }

        foreach (var change in step.Changes)
        {
            if (change.TableName == table.TableName && table.Contains(change.RecordId))
            {
                return change.RecordId;
            }
        }

        return null;
    }

    /// <summary>"1 item, 2 actions": how many records of each table the step changed.</summary>
    private static string Count(ContentEditStep step) =>
        string.Join(", ", step.Changes
            .GroupBy(change => change.TableName)
            .Select(group => $"{group.Count()} {(group.Count() == 1 ? ContentWorkspace.Singular(group.Key) : group.Key.ToLowerInvariant())}"));

    /// <summary>"Saved 1 to master, 1 to Drafts; removed 1." -- after why a table couldn't be written, when one couldn't.</summary>
    private static string Describe(ContentSaveReport report)
    {
        if (report.WriteFailure is { } failure)
        {
            return report.Results.Count == 0 && report.RemovedCount == 0 ? failure : $"{failure} Before it: {DescribeSaved(report)}";
        }

        return DescribeSaved(report);
    }

    private static string DescribeSaved(ContentSaveReport report)
    {
        var parts = new List<string>();
        var toMaster = report.Results.Count(result => result.Destination == ContentSaveDestination.Master);
        var toDrafts = report.Results.Count - toMaster;
        if (toMaster > 0)
        {
            parts.Add($"{toMaster} to master");
        }

        if (toDrafts > 0)
        {
            parts.Add($"{toDrafts} to Drafts");
        }

        var saved = parts.Count > 0 ? $"Saved {string.Join(", ", parts)}" : "Saved";
        var removed = report.RemovedCount > 0 ? $"; removed {report.RemovedCount}" : string.Empty;
        var kept = report.RefusedRemovals.Count > 0 ? $"; kept {report.RefusedRemovals.Count} deletion(s) in Drafts -- see the Error List" : string.Empty;
        return $"{saved}{removed}{kept}. Rebuilding.";
    }
}
