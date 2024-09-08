using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameComponents.MapBuilderComponent
{
    public class Grass : Entity
    {
        public Grass()
        {
            EntityData = new EntityData
            {
                Id = Guid.NewGuid(),
                Name = "Grass",
                Description = "Ordinary grass. Nothing special.",
                DisplayString = ",",
                BackgroundColor = Color.ForestGreen,
                ForegroundColor = Color.LawnGreen,
                DisplayStringOffset = new Point(5, -2)
            };
        }
    }
}
