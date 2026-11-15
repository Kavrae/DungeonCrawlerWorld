using Engine.Content;

namespace Workshop.Editing;

/// <summary>How a content field's display name reads in an editor: the label, and a trailing hint in parentheses, which goes to the tooltip.</summary>
public static class ContentFieldLabels
{
    /// <summary>The field's label, without its hint -- what an edit, a path or a message names it by.</summary>
    public static string LabelOf(ContentField field) => Split(field.DisplayName).Label;

    /// <summary>"Action lock in frames (none: the entity's own)" as ("Action lock in frames", "none: the entity's own"); a name with no trailing hint as itself, with none.</summary>
    public static (string Label, string? Hint) Split(string displayName)
    {
        if (!displayName.EndsWith(')'))
        {
            return (displayName, null);
        }

        var open = displayName.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0)
        {
            return (displayName, null);
        }

        var hint = displayName[(open + 2)..^1].Trim();
        return hint.Length == 0 ? (displayName, null) : (displayName[..open].TrimEnd(), char.ToUpperInvariant(hint[0]) + hint[1..]);
    }
}
