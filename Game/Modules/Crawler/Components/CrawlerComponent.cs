namespace Game.Modules.Crawler.Components;

/// <summary>
/// Marks an entity as a Crawler and carries its unique CrawlerNumber (assigned on entering the
/// dungeon, distinct from EntityId -- see CrawlerNumberAllocator). The player always has this;
/// only a small percentage of NPCs do (see TestMapBuilder.BuildRaceEntity) -- any active entity
/// without this component is an ordinary NPC.
/// </summary>
public struct CrawlerComponent(int crawlerNumber)
{
    public int CrawlerNumber { get; } = crawlerNumber;
}
