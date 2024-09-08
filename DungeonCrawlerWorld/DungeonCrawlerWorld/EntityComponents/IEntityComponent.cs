using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.EntityComponents
{
    public abstract class IEntityComponent
    {
        public Guid Id { get; set; }
        public Entity Entity { get; set; }

        public IEntityComponent()
        {
            Id = Guid.NewGuid();
        }
        public void SetEntity(Entity entity)
        {
            Entity = entity;
        }

        public abstract void Update(GameTime gameTime);

    }
}