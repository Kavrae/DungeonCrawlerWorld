namespace Workshop;

/// <summary>What the shell asks of a host whose tabs edit and save: whether anything is unsaved or unpublished, saving and publishing it, the work it does between frames, and the load problems a rebuild changes.</summary>
/// <remarks>
/// The shell adds Save (Ctrl+S, to drafts), Publish (Ctrl+Shift+S, to what the game reads), Undo (Ctrl+Z) and Redo
/// (Ctrl+Y), asks before quitting with
/// unsaved changes (Save, Discard, Cancel) and before playing with unpublished ones (Publish and Play, Play Anyway,
/// Cancel).
/// </remarks>
public interface IWorkshopEditing
{
    /// <summary>Whether anything changed that no save has written.</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>Whether anything isn't yet where the game reads it: unsaved, or saved only as a draft.</summary>
    bool HasUnpublishedChanges { get; }

    /// <summary>Saves everything unsaved as drafts; returns what the status line says. A file the disk refuses leaves what it held unsaved (HasUnsavedChanges).</summary>
    string Save();

    /// <summary>Tries to publish everything unpublished, saving what can't be as drafts; returns what the status line says. A file the disk refuses leaves what it held unsaved (HasUnsavedChanges).</summary>
    string Publish();

    /// <summary>Puts back typed text, else undoes the latest edit (Ctrl+Z), taking the player to it -- through navigator when another tab must open; returns what the status line says.</summary>
    string Undo(TabNavigator navigator);

    /// <summary>Redoes the edit undone last (Ctrl+Y), taking the player to it; returns what the status line says.</summary>
    string Redo(TabNavigator navigator);

    /// <summary>Called every frame before the tabs: a rebuilt session swapped in, say. Returns a status line when something finished, else null.</summary>
    string? Update();

    /// <summary>What went wrong loading what the tabs show, as of the latest rebuild.</summary>
    IReadOnlyList<WorkshopProblem> LoadProblems { get; }

    /// <summary>Changes whenever LoadProblems does.</summary>
    int LoadProblemsVersion { get; }
}
