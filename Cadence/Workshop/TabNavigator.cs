namespace Workshop;

/// <summary>Opens a tab with one of its records selected -- what a link to another table's record asks for.</summary>
/// <remarks>
/// A request is only queued; the shell carries it out at the start of its next update (ProcessPendingRequest). Opening a
/// tab closes the open one's windows, and the click that asked is still being handled inside one of them, so nothing is
/// closed from inside that click. The newest request wins. A tab that isn't in this session (the IDE started with
/// --tab=) or a record its tab doesn't hold is reported on the status line, never guessed at.
/// </remarks>
public sealed class TabNavigator(IReadOnlyList<IIdeTab> tabs, Action<int> openTab, IWorkshopStatus status)
{
    private (string TabTitle, Guid RecordId)? _pendingRequest;

    public void RequestOpenRecord(string tabTitle, Guid recordId) => _pendingRequest = (tabTitle, recordId);

    /// <summary>Carries out the queued request, if any: opens its tab and selects its record. False when there was none or it couldn't be done.</summary>
    public bool ProcessPendingRequest()
    {
        if (_pendingRequest is not { } request)
        {
            return false;
        }

        _pendingRequest = null;

        var tabIndex = IndexOfTab(request.TabTitle);
        if (tabIndex < 0)
        {
            status.Show($"The {request.TabTitle} tab isn't open in this session.");
            return false;
        }

        openTab(tabIndex);
        if (!tabs[tabIndex].TrySelectRecord(request.RecordId))
        {
            status.Show($"The {request.TabTitle} tab has no record {request.RecordId}.");
            return false;
        }

        return true;
    }

    private int IndexOfTab(string tabTitle)
    {
        for (var index = 0; index < tabs.Count; index++)
        {
            if (string.Equals(tabs[index].Title, tabTitle, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
