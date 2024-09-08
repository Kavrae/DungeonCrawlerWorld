using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameComponents.MapBuilderComponent
{
    public class Lava : Entity
    {
        public Lava()
        {
            EntityData = new EntityData
            {
                Id = Guid.NewGuid(),
                Name = "Lava",
                Description = "Hot lava. I do not recommend stepping on it.",
                DisplayString = "~",
                BackgroundColor = Color.OrangeRed,
                ForegroundColor = Color.Yellow,
                DisplayStringOffset = new Point(3, 0)
            };
        }
    }
}
