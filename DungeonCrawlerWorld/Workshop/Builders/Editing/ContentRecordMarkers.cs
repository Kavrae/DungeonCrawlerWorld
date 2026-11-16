namespace Builders.Editing;

/// <summary>How a record's state reads on its browser row: the markers after its name, what they mean, and whether the row is greyed.</summary>
public static class ContentRecordMarkers
{
    private const string UnsavedMarker = " *";
    private const string DisabledMarker = "[Disabled]";
    private const string ReplacedMarker = "[Replaced]";
    private const string DraftMarker = "[Draft]";
    private const string DeletedMarker = "[Deleted]";
    private const string ErrorMarker = " (!)";

    /// <summary>The markers for state in the order a row reads them -- " *", then " [Disabled][Replaced][Draft][Deleted]" as apply, then " (!)"; Replaced only for a disabled record something replaces.</summary>
    public static string Of(ContentRecordState state, string? replacementName)
    {
        var bracketed = (state.HasFlag(ContentRecordState.Disabled) ? DisabledMarker : string.Empty)
            + (state.HasFlag(ContentRecordState.Disabled) && replacementName is not null ? ReplacedMarker : string.Empty)
            + (state.HasFlag(ContentRecordState.Draft) ? DraftMarker : string.Empty)
            + (state.HasFlag(ContentRecordState.Deleted) ? DeletedMarker : string.Empty);
        return (state.HasFlag(ContentRecordState.Unsaved) ? UnsavedMarker : string.Empty)
            + (bracketed.Length > 0 ? " " + bracketed : string.Empty)
            + (state.HasFlag(ContentRecordState.HasErrors) ? ErrorMarker : string.Empty);
    }

    /// <summary>What each marker state shows means, a line each in the order they read, the replacement named; null for none.</summary>
    public static string? MeaningOf(ContentRecordState state, string? replacementName)
    {
        var lines = new List<string>(5);
        if (state.HasFlag(ContentRecordState.Unsaved))
        {
            lines.Add("*: unsaved changes");
        }

        if (state.HasFlag(ContentRecordState.Disabled))
        {
            lines.Add("Disabled: kept in master, never loaded");
            if (replacementName is not null)
            {
                lines.Add($"Replaced by {replacementName}");
            }
        }

        if (state.HasFlag(ContentRecordState.Draft))
        {
            lines.Add("Draft: not published to master");
        }

        if (state.HasFlag(ContentRecordState.Deleted))
        {
            lines.Add("Deleted: gone once the deletion is published or saved");
        }

        if (state.HasFlag(ContentRecordState.HasErrors))
        {
            lines.Add("(!): has errors");
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>A disabled or deleted record's row is greyed.</summary>
    public static bool IsDimmed(ContentRecordState state) => (state & (ContentRecordState.Disabled | ContentRecordState.Deleted)) != 0;
}
