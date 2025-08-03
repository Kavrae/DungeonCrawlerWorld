using System;

namespace DungeonCrawlerWorld.Components
{
    public interface IEntityComponent
    {
        Guid EntityId { get; set; }
    }
}