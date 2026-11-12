namespace UIEngine.UI;

/// <summary>Who is showing one shared popup: taken by every show, given up only by the owner that holds it.</summary>
/// <remarks>
/// Several consumers poll and each calls Hide when it has nothing to show; the check on TryRelease is what keeps one's
/// routine Hide from clearing what another is showing. The shared tooltip, the cursor's held text and the cursor's label
/// each keep one.
/// </remarks>
internal struct PopupOwnership
{
    private object? _owner;

    public readonly bool IsHeld => _owner is not null;

    /// <summary>Makes owner the one showing, whoever was before: a new show always replaces what was up.</summary>
    public void Take(object owner) => _owner = owner;

    /// <summary>Gives the popup up if owner is the one showing; returns whether it was.</summary>
    public bool TryRelease(object owner)
    {
        if (!ReferenceEquals(owner, _owner))
        {
            return false;
        }

        _owner = null;
        return true;
    }

    /// <summary>Gives the popup up whoever holds it.</summary>
    public void Clear() => _owner = null;
}
