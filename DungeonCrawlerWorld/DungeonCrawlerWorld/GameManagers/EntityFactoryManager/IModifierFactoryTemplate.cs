using System;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    public abstract class IModifierFactoryTemplate
    {
        public abstract void Apply(Guid entityId);
        public abstract void Remove(Guid entityId);
    }
}
