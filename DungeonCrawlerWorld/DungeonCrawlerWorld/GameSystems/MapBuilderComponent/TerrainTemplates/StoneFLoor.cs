using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameComponents.MapBuilderComponent
{
    public class StoneFloor : Entity
    {
        public StoneFloor()
        {
            EntityData = new EntityData
            {
                Id = Guid.NewGuid(),
                Name = "Stone Floor",
                Description = "Smooth stone floor.",
                DisplayString = null,
                BackgroundColor = Color.LightGray,
                ForegroundColor = Color.LightGray,
                DisplayStringOffset = new Point(0, 0)
            };
        }
    }
}
