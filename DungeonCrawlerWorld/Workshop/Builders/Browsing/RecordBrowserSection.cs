namespace Builders.Browsing;

/// <summary>One heading in a tab's browser and the records under it.</summary>
public sealed record RecordBrowserSection<TRecord>(string Heading, IReadOnlyList<TRecord> Records);
