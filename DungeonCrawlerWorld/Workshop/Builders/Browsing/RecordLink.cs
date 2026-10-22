namespace Builders.Browsing;

/// <summary>One line of a record's Related window: what it says, and the tab and record it opens -- or none, for content no tab shows (terrain, blueprints).</summary>
public readonly record struct RecordLink(string Text, string? TargetTabTitle, Guid TargetId)
{
    public bool OpensARecord => TargetTabTitle is not null;
}

/// <summary>A heading in the Related window ("Uses", "Used by") and its lines.</summary>
public sealed record RecordLinkSection(string Heading, IReadOnlyList<RecordLink> Links);
