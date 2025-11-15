using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that defines the background color used for the map tile the entity is on
    /// When multiple entities are on the same XY (but different Z) coordinates, the highest entity's backgroundComponent takes priority.
    /// </summary>
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
