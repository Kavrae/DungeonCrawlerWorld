using System;

namespace DungeonCrawlerWorld.GameManagers.EntityFactoryManager
{
    //TODO split into race, class, terrain, and object templates.
    //Race template adds the race to ComponentRepo, picks a name, etc.
    //Class tempaltes adds the class to ComponentRepo.
    //Terrain and objects don't need to register.

    //TODO override - just make race another component, but as an abstract class. Make each individual race override it.
    //Then do the same for class, but it's an array of classes.
    public abstract class IBaseFactoryTemplate
    {
        public abstract Guid Id { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract void Build(Guid entityId);
    }
}
