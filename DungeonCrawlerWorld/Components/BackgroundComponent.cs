using System;

using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Components
{
    public struct BackgroundComponent : IEntityComponent
    {
        public int EntityId { get; set; }
        public Color? BackgroundColor { get; set; }

        public BackgroundComponent(int entityId) : this(entityId, Color.Black) { }
        public BackgroundComponent(int entityId, Color? backgroundColor)
        {
            EntityId = entityId;
            BackgroundColor = backgroundColor;

            ComponentRepo.BackgroundComponents[entityId] = this;
        }

        public override string ToString()
        {
            return $"Background : {BackgroundColor}";
        }
    }
}
