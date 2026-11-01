namespace Game.World;

/// <summary>Who an entity was when something recorded it as a source or a killer: its name, for a Crawler its crawler number, and what it acted through (Cause).</summary>
/// <remarks>Read through EntityIdentities by handle, so it outlives the entity's unload.</remarks>
public readonly record struct EntityIdentity(string? Name, int? CrawlerNumber, SourceCause Cause = default)
{
    /// <summary>"Name", "Name (Crawler #N)", or "Unknown" when nothing was recorded.</summary>
    public string DisplayName => (Name, CrawlerNumber) switch
    {
        (null, null) => "Unknown",
        (null, { } number) => $"Crawler #{number}",
        ({ } name, null) => name,
        ({ } name, { } number) => $"{name} (Crawler #{number})",
    };
}
