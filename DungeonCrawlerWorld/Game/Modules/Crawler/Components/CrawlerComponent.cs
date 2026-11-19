using Engine.ECS.Properties;

namespace Game.Modules.Crawler.Components;

/// <summary> Marks an entity as a Crawler and carries its unique CrawlerNumber </summary>
/// <cleanupVersion>1</cleanupVersion>
[DescribedComponent]
public readonly partial struct CrawlerComponent(int crawlerNumber)
{
    /// <summary>The unique identifier assigned sequentially to every crawler the moment they enter the dungeon.</summary>
    public int CrawlerNumber { get; init; } = crawlerNumber;
}
