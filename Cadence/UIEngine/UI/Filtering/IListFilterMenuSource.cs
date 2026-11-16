namespace UIEngine.UI.Filtering;

/// <summary>One row of a filter's menu: a group's heading, or a choice with the count of items a flip of it would leave.</summary>
/// <param name="ChoiceIndex">The choice's index in the selection; -1 for a heading.</param>
public readonly record struct ListFilterMenuRow(string Label, int ChoiceIndex, int CountIfFlipped)
{
    public bool IsHeading => ChoiceIndex < 0;
}

/// <summary>What a filter button's menu reads, whatever the type of item the filter tests: the selection it flips, and its rows with their counts.</summary>
public interface IListFilterMenuSource
{
    ListFilterSelection Selection { get; }

    /// <summary>Every group's heading and choices in order, each choice counted over the host's items as they are now.</summary>
    IReadOnlyList<ListFilterMenuRow> MenuRows();

    /// <summary>The label of the choice at choiceIndex, as the menu and a chip show it.</summary>
    string ChoiceLabel(int choiceIndex);
}
