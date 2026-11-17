using UIEngine.UI.Filtering;

namespace Builders.Editing;

/// <summary>The filter groups every editing tab offers over its table's records.</summary>
public static class ContentRecordFilters
{
    public const string ShowOnlyGroupId = "show";
    public const string ShowOnlyHeading = "Show only";

    /// <summary>Show only: a choice per fact a row is marked with, each read from the record's state (table.StateOf), the rule its markers read; every checked one must hold.</summary>
    /// <remarks>A marked fact's choice takes its label, icon and tooltip from its marker's symbol, so the menu, the chip and the row say the same.</remarks>
    public static ListFilterGroup<ContentRecordDocument<T>> ShowOnly<T>(ContentEditingTable<T> table)
        where T : class =>
        new(ShowOnlyGroupId, ShowOnlyHeading, ListFilterGroupMatch.AllOf,
        [
            MarkerChoice(table, "show:unsaved", ContentRecordMarker.Unsaved, ContentRecordState.Unsaved),
            MarkerChoice(table, "show:draft", ContentRecordMarker.Draft, ContentRecordState.Draft),
            StateChoice(table, "show:differs", "Differs from master", ContentRecordState.DiffersFromMaster),
            MarkerChoice(table, "show:errors", ContentRecordMarker.HasErrors, ContentRecordState.HasErrors),
            MarkerChoice(table, "show:disabled", ContentRecordMarker.Disabled, ContentRecordState.Disabled),
            MarkerChoice(table, "show:replaced", ContentRecordMarker.Replaced, ContentRecordState.Replaced),
            MarkerChoice(table, "show:deleted", ContentRecordMarker.Deleted, ContentRecordState.Deleted),
        ]);

    private static ListFilterChoice<ContentRecordDocument<T>> MarkerChoice<T>(ContentEditingTable<T> table, string id, ContentRecordMarker marker, ContentRecordState state)
        where T : class
    {
        var symbol = ContentRecordMarkers.SymbolOf(marker);
        return new(id, symbol.Label, document => (table.StateOf(document) & state) != 0) { Icon = symbol.Icon, TooltipText = symbol.Tooltip };
    }

    private static ListFilterChoice<ContentRecordDocument<T>> StateChoice<T>(ContentEditingTable<T> table, string id, string label, ContentRecordState state)
        where T : class =>
        new(id, label, document => (table.StateOf(document) & state) != 0);
}
