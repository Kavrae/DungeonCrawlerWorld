using System;

namespace DungeonCrawlerWorld.Data.Blueprints
{
    /// <summary>
    /// Represents a blueprint for creating game entities with a pre-defined component list and properties
    /// Blueprints are meant to be nested for building complex entities
    /// </summary>
    public interface IBlueprint
    {
        public static abstract void Build(int entityId);
    }
}
