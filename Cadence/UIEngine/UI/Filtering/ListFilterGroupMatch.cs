namespace UIEngine.UI.Filtering;

/// <summary>How a group's checked choices combine: each must hold, or any one may.</summary>
/// <remarks>
/// AllOf is for independent yes/no facts about an item (a draft, one with errors: both checked lists the drafts with
/// errors); AnyOf is for a group whose choices are the values of one property, of which an item has exactly one (a
/// source, a kind: two checked lists both). A group with nothing checked lets everything through either way.
/// </remarks>
public enum ListFilterGroupMatch
{
    AllOf,
    AnyOf,
}
