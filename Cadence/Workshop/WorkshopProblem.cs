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
}

/// <summary>One line of Dependents: a record that names the one selected, its kind, how it names it, and the tab that shows it, when one does.</summary>
public sealed record WorkshopDependent(string Name, string Kind, string Relationship)
{
    public string? TabTitle { get; init; }

    public Guid? RecordId { get; init; }
}

/// <summary>What a host's IDE opens with: its tabs and what went wrong loading what they show.</summary>
public sealed record WorkshopTabSet(IReadOnlyList<IIdeTab> Tabs, IReadOnlyList<WorkshopProblem> LoadProblems);
