using Builders.Editing;
using Game.Bootstrap;
using Workshop;

namespace DungeonCrawlerWorld.Workshop;

/// <summary>The shell's view of the game's editing: what is unsaved, saving, publishing, undo and redo, the rebuilt session swapped in between frames, and the load problems of the session now -- less those of records a tab holds open, which show their own.</summary>
internal sealed class WorkshopContentEditing(ContentEditing editing, Func<GameSession, IReadOnlyList<WorkshopProblem>> loadProblemsOf) : IWorkshopEditing
{
    private IReadOnlyList<WorkshopProblem>? _loadProblems;
    private int _loadProblemsSessionVersion = -1;

    private ContentWorkspace Workspace => editing.Workspace;

    public bool HasUnsavedChanges => Workspace.HasUnsavedChanges;

    public bool HasUnpublishedChanges => Workspace.HasUnpublishedChanges;

    public string Save() => editing.Save();

    public string Publish() => editing.PublishAll();

    public string Undo(TabNavigator navigator) => editing.Undo(navigator);

    public string Redo(TabNavigator navigator) => editing.Redo(navigator);

    public string? Update() => editing.Update();

    public int LoadProblemsVersion => Workspace.SessionVersion;

    public IReadOnlyList<WorkshopProblem> LoadProblems
    {
        get
        {
            if (_loadProblems is null || _loadProblemsSessionVersion != Workspace.SessionVersion)
            {
                _loadProblemsSessionVersion = Workspace.SessionVersion;
                _loadProblems = [.. loadProblemsOf(Workspace.Session).Where(problem => !IsHeldOpen(problem))];
            }

            return _loadProblems;
        }
    }

    private bool IsHeldOpen(WorkshopProblem problem) =>
        problem.RecordId is { } id && Workspace.Tables.Any(table => table.TabTitle == problem.TabTitle && table.Contains(id));
}
