namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// Defines a component of an entity.
    /// A component ontains ONLY properties for systems to act upon. Never logic.
    /// </summary>
    /// <warning>
    /// Keep component sizes under multiples of 16 bytes whenever possible to take advantage of processor cache retrieval and avoid cache misses.
    /// </warning>
    public interface IEntityComponent
    {
        int EntityId { get; set; }
    }
}