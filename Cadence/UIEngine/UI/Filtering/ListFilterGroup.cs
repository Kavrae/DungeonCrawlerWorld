namespace UIEngine.UI.Filtering;

/// <summary>A heading's worth of filter choices, and how the ones checked combine.</summary>
public sealed class ListFilterGroup<TItem>(string id, string heading, ListFilterGroupMatch match, IReadOnlyList<ListFilterChoice<TItem>> choices)
{
    public string Id { get; } = id;

    /// <summary>The host's text for the group's heading, which says how its choices combine ("Show only", "Source: any of").</summary>
    public string Heading { get; } = heading;

    public ListFilterGroupMatch Match { get; } = match;

    public IReadOnlyList<ListFilterChoice<TItem>> Choices { get; } = choices;
}
