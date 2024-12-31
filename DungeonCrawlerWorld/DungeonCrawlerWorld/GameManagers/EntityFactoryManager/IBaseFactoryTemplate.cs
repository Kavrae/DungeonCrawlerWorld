using System;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public abstract class IBaseFactoryTemplate
    {
        public abstract void Build(Guid entityId);
    }
}
