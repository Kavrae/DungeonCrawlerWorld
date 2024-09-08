using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameComponents.EntityFactoryComponent
{
    public class Wall : Entity
    {
        public Wall()
        {
            EntityData = new EntityData
            {
                Description = "Basic wall. Default implementation.",
                DisplayString = "[][]",
                DisplayStringOffset = new Point(0, -1),
                ForegroundColor = Color.DarkGray,
                Id = Guid.NewGuid(),
                Name = "Wall (Default)"
            };
        }

        public Wall(EntityData entityData)
        {
            EntityData = entityData;
        }
    }
}
