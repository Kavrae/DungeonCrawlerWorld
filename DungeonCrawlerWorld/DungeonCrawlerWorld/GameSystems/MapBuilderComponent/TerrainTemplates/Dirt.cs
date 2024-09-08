using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameComponents.MapBuilderComponent
{
    public class Dirt : Entity
    {
        public Dirt()
        {
            EntityData = new EntityData
            {
                Id = Guid.NewGuid(),
                Name = "Dirt",
                Description = "Ordinary dirt. Nothing special.",
                DisplayString = null,
                BackgroundColor = Color.Tan,
                ForegroundColor = Color.Tan,
                DisplayStringOffset = new Point(0, 0)
            };
        }
    }
}
