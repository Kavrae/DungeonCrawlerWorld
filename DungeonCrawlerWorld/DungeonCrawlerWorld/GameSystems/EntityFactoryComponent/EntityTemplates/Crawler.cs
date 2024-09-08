using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameComponents.EntityFactoryComponent
{
    public class Crawler : Entity
    {
        public Crawler()
        {
            EntityData = new EntityData
            {
                Description = "Crawler entity. Default implementation.",
                DisplayString = "O",
                DisplayStringOffset = new Point(2, 0),
                ForegroundColor = Color.White,
                Id = Guid.NewGuid(),
                Name = "Crawler (Default)"
            };
        }

        public Crawler(EntityData entityData) : base(entityData)
        {
            EntityData = entityData;
        }
    }
}
