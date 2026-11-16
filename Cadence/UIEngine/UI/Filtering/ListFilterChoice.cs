namespace UIEngine.UI.Filtering;

/// <summary>One choice a filter offers: a stable id, the label the host shows for it, and whether an item matches it.</summary>
/// <remarks>
/// The id names the choice wherever it is kept (a host's saved settings) and is never shown. The predicate is built once,
/// with the choice, and must not allocate, since a host tests every item against it whenever its list rebuilds.
/// </remarks>
public sealed class ListFilterChoice<TItem>(string id, string label, Func<TItem, bool> matches)
{
    public string Id { get; } = id;

    public string Label { get; } = label;

    public Func<TItem, bool> Matches { get; } = matches;

    /// <summary>What the host shows when the choice is hovered; none by default.</summary>
    public string? TooltipText { get; init; }
}
