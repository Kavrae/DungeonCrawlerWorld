namespace Game.Modules.Crawler.Components;

/// <summary> Marks an entity as a Crawler and carries its unique CrawlerNumber </summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly struct CrawlerComponent(int crawlerNumber)
{
    /// <summary>The unique identifier assigned sequentially to every crawler the moment they enter the dungeon.</summary>
    public int CrawlerNumber { get; } = crawlerNumber;

    public override readonly string ToString() => $"CrawlerNumber : {CrawlerNumber}";
}
