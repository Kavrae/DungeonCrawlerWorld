namespace Presentation;

/// <summary>
/// Tracks whether a version counter (e.g. MultiComponentPool.GetEntityVersion) has changed
/// since the last check -- the "rebuild only when the underlying data actually changed" idiom
/// content panes use to avoid rebuilding every frame, generalized so consumers don't each
/// hand-roll their own hasChecked+lastSeenVersion bookkeeping.
/// </summary>
public sealed class VersionWatcher
{
    private uint _lastSeenVersion;
    private bool _hasChecked;

    /// <summary>True the first time it's called (nothing to compare against yet) or whenever currentVersion differs from the last-seen value; false otherwise. Updates the last-seen value as a side effect either way, so the next call compares against this one.</summary>
    public bool HasChanged(uint currentVersion)
    {
        var changed = !_hasChecked || currentVersion != _lastSeenVersion;

        _hasChecked = true;
        _lastSeenVersion = currentVersion;

        return changed;
    }
}
