using UIEngine.UI.Filtering;

namespace Workshop.Sprites;

/// <summary>How the Sprites tab narrows its entries: a search by name, and its filter groups -- Show only (Has problems), then Kind.</summary>
public static class SpriteEntryFilters
{
    public const string HasProblemsChoiceId = "show:problems";

    /// <summary>The entries whose name holds search, ignoring case, in entries' order; every entry for an empty search.</summary>
    public static List<SpriteEntryView> Search(IEnumerable<SpriteEntryView> entries, string search) =>
        [.. entries.Where(entry => search.Length == 0 || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase))];

    /// <summary>Show only: Has problems (what the Error List names for an entry). Kind: Options, Linked set, Wall -- any of.</summary>
    public static IReadOnlyList<ListFilterGroup<SpriteEntryView>> Groups { get; } =
    [
        new ListFilterGroup<SpriteEntryView>("show", "Show only", ListFilterGroupMatch.AllOf,
        [
            new ListFilterChoice<SpriteEntryView>(HasProblemsChoiceId, "Has problems", static entry => entry.Problems.Count > 0),
        ]),
        new ListFilterGroup<SpriteEntryView>("kind", "Kind: any of", ListFilterGroupMatch.AnyOf,
        [
            new ListFilterChoice<SpriteEntryView>("kind:options", "Options", static entry => entry.Kind == SpriteEntryKind.Options),
            new ListFilterChoice<SpriteEntryView>("kind:linked-set", "Linked set", static entry => entry.Kind == SpriteEntryKind.LinkedSet),
            new ListFilterChoice<SpriteEntryView>("kind:wall", "Wall", static entry => entry.Kind == SpriteEntryKind.Wall),
        ]),
    ];
}
