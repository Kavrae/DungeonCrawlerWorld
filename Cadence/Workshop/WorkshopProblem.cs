namespace Workshop;

public enum WorkshopProblemSeverity : byte
{
    Error,
    Warning,
    Message,
}

/// <summary>One line of the Error List: how bad it is, what is wrong, where -- a file and line, a mod -- and the record it is about, when a tab shows one.</summary>
public sealed record WorkshopProblem(WorkshopProblemSeverity Severity, string Description, string Source)
{
    /// <summary>The tab showing the record this is about; with RecordId, what activating the line opens.</summary>
    public string? TabTitle { get; init; }

    public Guid? RecordId { get; init; }

    /// <summary>The field of the record it is about ("fx[0].e[1].c"), which activating the line focuses; null for the whole record.</summary>
    public string? FieldPath { get; init; }
}

/// <summary>One line of Used By or Uses: a record that names the one selected, or that it names, its kind, how, and the tab that shows it, when one does.</summary>
public sealed record WorkshopRelatedRecord(string Name, string Kind, string Relationship)
{
    public string? TabTitle { get; init; }

    public Guid? RecordId { get; init; }
}

/// <summary>One line of Changes: a field of the selected record, how it differs from the baseline, and each side's value.</summary>
/// <param name="Change">"Changed", "Added", "Removed" or "Moved".</param>
public sealed record WorkshopFieldChange(string Field, string Change, string Baseline, string Current)
{
    public string? TabTitle { get; init; }

    public Guid? RecordId { get; init; }

    /// <summary>The field a click on the line focuses; null for none.</summary>
    public string? FieldPath { get; init; }

    /// <summary>Puts the field back as the baseline has it, as one undo step; null when it can't be.</summary>
    public Action? Revert { get; init; }
}

/// <summary>What Changes shows for the selected record: what it is compared with ("Master"), each field that differs, and the line shown when none does (why).</summary>
public sealed record WorkshopChanges(string BaselineName, IReadOnlyList<WorkshopFieldChange> Changes, string NothingChangedText);

/// <summary>One line of To Publish: a record a publish would write to master, its table, what changes ("New", "3 fields changed", "Deleted") and where it stands ("Unsaved", "Saved as draft", "Has errors: stays a draft").</summary>
public sealed record WorkshopPendingRecord(string Name, string Table, string Change, string State)
{
    public string? TabTitle { get; init; }

    public Guid? RecordId { get; init; }
}

/// <summary>What a host's IDE opens with: its tabs and what went wrong loading what they show.</summary>
public sealed record WorkshopTabSet(IReadOnlyList<IIdeTab> Tabs, IReadOnlyList<WorkshopProblem> LoadProblems)
{
    /// <summary>How the host saves what its tabs edit; null for a host that edits nothing (no Save, no prompts).</summary>
    public IWorkshopEditing? Editing { get; init; }
}
