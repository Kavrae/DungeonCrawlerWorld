using System;

using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Components
{
    public struct BackgroundComponent
    {
        public Guid EntityId { get; set; }
        public Color? BackgroundColor { get; set; }

        public BackgroundComponent(Guid entityId) : this(entityId, Color.Black) { }
        public BackgroundComponent(Guid entityId, Color? backgroundColor)
        {
            EntityId = entityId;
            BackgroundColor = backgroundColor;

            ComponentRepo.BackgroundComponents.Remove(entityId);
            ComponentRepo.BackgroundComponents.Add(entityId, this);
        }

        public override string ToString()
        {
            return $"Background : {BackgroundColor}";
        }
    }
}
