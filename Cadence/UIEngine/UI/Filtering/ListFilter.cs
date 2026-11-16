namespace UIEngine.UI.Filtering;

/// <summary>A list's filter: its groups of choices, which are checked, whether an item matches, and how many items a flip of one choice would leave.</summary>
/// <remarks>
/// <para>
/// An item matches when every group lets it through: a group with nothing checked always does, an AllOf group when every
/// checked choice holds, an AnyOf group when any one does. A host combines that with anything else narrowing its list (a
/// search) by asking only about the items that are left.
/// </para>
/// <para>
/// The filter holds no items: the host passes its list whenever it asks, so a list that changes is simply asked again.
/// Matching one item allocates nothing. Choices are indexed in order across every group, as the selection is.
/// </para>
/// </remarks>
public sealed class ListFilter<TItem>
{
    private const int NoFlippedChoice = -1;

    private readonly ListFilterChoice<TItem>[] _choices;
    private readonly int[] _groupStartIndices;

    public ListFilter(IReadOnlyList<ListFilterGroup<TItem>> groups)
    {
        Groups = groups;
        _groupStartIndices = new int[groups.Count + 1];
        var choices = new List<ListFilterChoice<TItem>>();
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            _groupStartIndices[groupIndex] = choices.Count;
            choices.AddRange(groups[groupIndex].Choices);
        }

        _groupStartIndices[groups.Count] = choices.Count;
        _choices = [.. choices];
        Selection = new ListFilterSelection([.. _choices.Select(choice => choice.Id)]);
    }

    public IReadOnlyList<ListFilterGroup<TItem>> Groups { get; }

    /// <summary>Every group's choices in order, at the indices the selection uses.</summary>
    public IReadOnlyList<ListFilterChoice<TItem>> Choices => _choices;

    public ListFilterSelection Selection { get; }

    /// <summary>The index of the first of group groupIndex's choices.</summary>
    public int FirstChoiceIndexOf(int groupIndex) => _groupStartIndices[groupIndex];

    public bool Matches(TItem item) => Matches(item, NoFlippedChoice);

    /// <summary>How many of items match.</summary>
    public int CountMatching(IReadOnlyList<TItem> items) => CountMatching(items, NoFlippedChoice);

    /// <summary>How many of items would match were choice choiceIndex flipped, every other choice as it is.</summary>
    public int CountMatchingIfFlipped(int choiceIndex, IReadOnlyList<TItem> items) => CountMatching(items, choiceIndex);

    private int CountMatching(IReadOnlyList<TItem> items, int flippedChoiceIndex)
    {
        var count = 0;
        for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            if (Matches(items[itemIndex], flippedChoiceIndex))
            {
                count++;
            }
        }

        return count;
    }

    private bool Matches(TItem item, int flippedChoiceIndex)
    {
        for (var groupIndex = 0; groupIndex < Groups.Count; groupIndex++)
        {
            if (!GroupLetsThrough(groupIndex, item, flippedChoiceIndex))
            {
                return false;
            }
        }

        return true;
    }

    private bool GroupLetsThrough(int groupIndex, TItem item, int flippedChoiceIndex)
    {
        var isAllOf = Groups[groupIndex].Match == ListFilterGroupMatch.AllOf;
        var hasCheckedChoice = false;
        for (var choiceIndex = _groupStartIndices[groupIndex]; choiceIndex < _groupStartIndices[groupIndex + 1]; choiceIndex++)
        {
            if (Selection.IsChecked(choiceIndex) == (choiceIndex == flippedChoiceIndex))
            {
                continue;
            }

            hasCheckedChoice = true;
            var matchesChoice = _choices[choiceIndex].Matches(item);
            if (isAllOf && !matchesChoice)
            {
                return false;
            }

            if (!isAllOf && matchesChoice)
            {
                return true;
            }
        }

        return isAllOf || !hasCheckedChoice;
    }
}
