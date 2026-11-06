using Game.Modules.Actions;
using Game.Modules.Inventory;

namespace Game.Content;

/// <summary>Builds a catalog's summaries: the content's, and those of definitions registered beside it (one made during the session, a loot box).</summary>
internal static class DefinitionSummaries
{
    public static DefinitionSummary Of(ActionDefinition action) =>
        new(action.Id, action.Name, action.SpriteName, action.DeclaredTags, CanTrade: true, HasContents: false);

    public static DefinitionSummary Of(ItemDefinition item) =>
        new(item.Id, item.Name, item.SpriteName, item.DeclaredTags, item.CanTrade, item.Contents is not null);

    /// <summary>source's summaries, then each registered definition source doesn't name, by Guid.</summary>
    public static IReadOnlyList<DefinitionSummary> Of<T>(Func<IReadOnlyList<DefinitionSummary>>? source, IEnumerable<T> registered, Func<T, DefinitionSummary> summarize)
    {
        if (source is null)
        {
            return [.. registered.Select(summarize).OrderBy(summary => summary.Id)];
        }

        var summaries = source();
        var named = summaries.Select(summary => summary.Id).ToHashSet();
        var beside = registered.Select(summarize).Where(summary => !named.Contains(summary.Id)).ToList();
        return beside.Count == 0 ? summaries : [.. summaries.Concat(beside).OrderBy(summary => summary.Id)];
    }
}

/// <summary>A catalog's merged summaries, built once and again only when its registrations change (Catalog.Version).</summary>
/// <remarks>The content's summaries are fixed for a session, so a catalog's registrations are all that can change the merge.</remarks>
internal sealed class CachedDefinitionSummaries
{
    private IReadOnlyList<DefinitionSummary>? _summaries;
    private int _builtAtVersion = -1;

    /// <summary>Forgets what was built, for when the source the summaries are merged from changes.</summary>
    public void Clear() => _summaries = null;

    public IReadOnlyList<DefinitionSummary> Get(int catalogVersion, Func<IReadOnlyList<DefinitionSummary>> build)
    {
        if (_summaries is null || _builtAtVersion != catalogVersion)
        {
            _summaries = build();
            _builtAtVersion = catalogVersion;
        }

        return _summaries;
    }
}
