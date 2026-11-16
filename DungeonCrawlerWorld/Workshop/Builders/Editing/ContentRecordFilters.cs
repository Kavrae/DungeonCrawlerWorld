using UIEngine.UI.Filtering;

namespace Builders.Editing;

/// <summary>The filter groups every editing tab offers over its table's records.</summary>
public static class ContentRecordFilters
{
    public const string ShowOnlyGroupId = "show";
    public const string ShowOnlyHeading = "Show only";

    /// <summary>Show only: a choice per fact a row is marked with, each read from the record's state (table.StateOf), the rule its markers read; every checked one must hold.</summary>
    public static ListFilterGroup<ContentRecordDocument<T>> ShowOnly<T>(ContentEditingTable<T> table)
        where T : class =>
        new(ShowOnlyGroupId, ShowOnlyHeading, ListFilterGroupMatch.AllOf,
        [
            StateChoice(table, "show:unsaved", "Unsaved", ContentRecordState.Unsaved),
            StateChoice(table, "show:draft", "Draft", ContentRecordState.Draft),
            StateChoice(table, "show:differs", "Differs from master", ContentRecordState.DiffersFromMaster),
            StateChoice(table, "show:errors", "Has errors", ContentRecordState.HasErrors),
            StateChoice(table, "show:disabled", "Disabled", ContentRecordState.Disabled),
            StateChoice(table, "show:deleted", "Deleted", ContentRecordState.Deleted),
        ]);

    private static ListFilterChoice<ContentRecordDocument<T>> StateChoice<T>(ContentEditingTable<T> table, string id, string label, ContentRecordState state)
        where T : class =>
        new(id, label, document => (table.StateOf(document) & state) != 0);
}
