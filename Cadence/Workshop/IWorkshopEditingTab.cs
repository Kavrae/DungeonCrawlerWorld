namespace Workshop;

/// <summary>A tab that edits records: whether it holds changes not yet saved (its tile shows "*"), the problems its edits have now, and how to take the player to one field.</summary>
/// <remarks>Its own interface rather than default members on IIdeTab, so a tab that drifts from it fails to compile instead of quietly keeping a default.</remarks>
public interface IWorkshopEditingTab : IIdeTab
{
    bool HasUnsavedChanges { get; }

    /// <summary>The Error List's lines for what the tab holds now, each naming its record and field.</summary>
    IReadOnlyList<WorkshopProblem> Problems { get; }

    /// <summary>Changes whenever Problems or PendingPublish does, so the shell rebuilds the Error List and To Publish only then.</summary>
    int ProblemsVersion { get; }

    /// <summary>To Publish's lines for the tab: every record a publish would write to master -- new, changed, disabled or deleted -- and whether it would go.</summary>
    IReadOnlyList<WorkshopPendingRecord> PendingPublish { get; }

    /// <summary>Takes the player to fieldPath of the record now selected: opens what hides it and focuses its control.</summary>
    void FocusField(string fieldPath);
}
