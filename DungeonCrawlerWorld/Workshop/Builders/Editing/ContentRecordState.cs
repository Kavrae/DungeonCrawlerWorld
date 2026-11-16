namespace Builders.Editing;

/// <summary>What is true of a record the Workshop holds: the facts its browser row is marked with and its filters test.</summary>
/// <remarks>
/// Worked out in one place, ContentEditingTable.StateOf, so a row's markers and a filter on the same fact can never list
/// different records. A mod's record, shown and never edited, is none of these.
/// </remarks>
[Flags]
public enum ContentRecordState
{
    None = 0,

    /// <summary>A save would write it: it changed, or its deletion isn't saved yet.</summary>
    Unsaved = 1 << 0,

    /// <summary>Saved as a draft, or not in master at all.</summary>
    Draft = 1 << 1,

    /// <summary>Kept whole in master but never loaded ("off").</summary>
    Disabled = 1 << 2,

    /// <summary>Deleted, the deletion not yet final.</summary>
    Deleted = 1 << 3,

    /// <summary>Something stops it, or the last save kept it in Drafts.</summary>
    HasErrors = 1 << 4,

    /// <summary>A publish would write it: master doesn't hold it as it is, or a deletion would take master's line away -- what the To Publish list lists.</summary>
    DiffersFromMaster = 1 << 5,
}
