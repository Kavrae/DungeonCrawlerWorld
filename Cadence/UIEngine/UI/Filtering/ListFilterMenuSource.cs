namespace UIEngine.UI.Filtering;

/// <summary>A filter's menu over a host's items: each choice counted over itemsToFilter, the items whatever else narrows the list (a search) has left.</summary>
/// <remarks>Counts are worked out each time MenuRows is asked -- when the menu opens and after each change -- never per frame.</remarks>
public sealed class ListFilterMenuSource<TItem>(ListFilter<TItem> filter, Func<IReadOnlyList<TItem>> itemsToFilter) : IListFilterMenuSource
{
    public ListFilterSelection Selection => filter.Selection;

    public string ChoiceLabel(int choiceIndex) => filter.Choices[choiceIndex].Label;

    public GlyphIcon? ChoiceIcon(int choiceIndex) => filter.Choices[choiceIndex].Icon;

    public string? ChoiceTooltip(int choiceIndex) => filter.Choices[choiceIndex].TooltipText;

    public IReadOnlyList<ListFilterMenuRow> MenuRows()
    {
        var items = itemsToFilter();
        var rows = new List<ListFilterMenuRow>(filter.Groups.Count + filter.Choices.Count);
        for (var groupIndex = 0; groupIndex < filter.Groups.Count; groupIndex++)
        {
            var group = filter.Groups[groupIndex];
            rows.Add(new ListFilterMenuRow(group.Heading, -1, 0));
            var firstChoiceIndex = filter.FirstChoiceIndexOf(groupIndex);
            for (var offset = 0; offset < group.Choices.Count; offset++)
            {
                var choiceIndex = firstChoiceIndex + offset;
                var choice = group.Choices[offset];
                rows.Add(new ListFilterMenuRow(choice.Label, choiceIndex, filter.CountMatchingIfFlipped(choiceIndex, items)) { Icon = choice.Icon, TooltipText = choice.TooltipText });
            }
        }

        return rows;
    }
}
