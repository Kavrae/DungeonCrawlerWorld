namespace Workshop.Editing;

/// <summary>One record's JSON before and after a step; null where the record doesn't exist (before it was made, after it was deleted).</summary>
public sealed record ContentEditChange(string TableName, Guid RecordId, string? Before, string? After);

/// <summary>One undoable step: what it was ("Change Gold value"), the record the user acted on (its origin), and every record it changed.</summary>
/// <param name="MergeKey">Steps with the same key one after another become one -- the wheel stepping a number, the same field committed again -- until merging is sealed; null never merges.</param>
/// <param name="OriginFieldPath">The field of the origin record the step changed, which undo and redo take the player back to; null for a step on whole records (New, Delete, Disable).</param>
public sealed record ContentEditStep(string Description, string OriginTableName, Guid OriginRecordId, IReadOnlyList<ContentEditChange> Changes, string? MergeKey = null, string? OriginFieldPath = null);

/// <summary>
/// The undo and redo history of a set of records that can name one another -- every content table the editor holds -- so a
/// step that changes records in several tables is one step, undone at once.
/// </summary>
/// <remarks>
/// The log only keeps JSON; the owner puts a step's Before (undo) or After (redo) back into its records. A new step
/// clears what could be redone. A step whose records end where they began is dropped.
/// </remarks>
public sealed class ContentEditLog
{
    private readonly List<ContentEditStep> _undo = [];
    private readonly List<ContentEditStep> _redo = [];
    private bool _isMergingSealed = true;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Changes whenever a step is recorded, undone or redone.</summary>
    public int Version { get; private set; }

    /// <summary>The step undo would take back, if any.</summary>
    public ContentEditStep? NextUndo => _undo.Count > 0 ? _undo[^1] : null;

    public void Record(ContentEditStep step)
    {
        _redo.Clear();
        Version++;
        if (!_isMergingSealed && step.MergeKey is { } key && _undo.Count > 0 && _undo[^1].MergeKey == key && SameRecords(_undo[^1], step))
        {
            var merged = Merge(_undo[^1], step);
            _undo.RemoveAt(_undo.Count - 1);
            if (!IsNoChange(merged))
            {
                _undo.Add(merged);
            }

            return;
        }

        _isMergingSealed = false;
        if (!IsNoChange(step))
        {
            _undo.Add(step);
        }
    }

    /// <summary>Ends merging: the next step starts a step of its own whatever its key -- the user left the field.</summary>
    public void SealMerging() => _isMergingSealed = true;

    /// <summary>The step to undo, moved to what can be redone; null when there is none. The caller puts each change's Before back.</summary>
    public ContentEditStep? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step);
        _isMergingSealed = true;
        Version++;
        return step;
    }

    /// <summary>The step to redo, moved back to what can be undone; null when there is none. The caller puts each change's After back.</summary>
    public ContentEditStep? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);
        _isMergingSealed = true;
        Version++;
        return step;
    }

    private static bool SameRecords(ContentEditStep first, ContentEditStep second) =>
        first.Changes.Count == second.Changes.Count && first.Changes.Zip(second.Changes).All(pair => pair.First.RecordId == pair.Second.RecordId && pair.First.TableName == pair.Second.TableName);

    private static ContentEditStep Merge(ContentEditStep earlier, ContentEditStep later) =>
        later with { Changes = [.. earlier.Changes.Zip(later.Changes, (first, second) => first with { After = second.After })] };

    private static bool IsNoChange(ContentEditStep step) => step.Changes.All(change => change.Before == change.After);
}
