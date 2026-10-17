namespace Game.World;

/// <summary>Who an entity was when something recorded it as a source or a killer: its name and, for a Crawler, its crawler number.</summary>
/// <remarks>Read through EntityIdentities by handle, so it outlives the entity's unload.</remarks>
public readonly record struct EntityIdentity(string? Name, int? CrawlerNumber)
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
