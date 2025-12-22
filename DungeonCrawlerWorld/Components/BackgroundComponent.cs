using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that defines the background color used for the map tile the entity is on
    /// When multiple entities are on the same XY (but different Z) coordinates, the highest entity's backgroundComponent takes priority.
    /// </summary>
    public struct BackgroundComponent(Color backgroundColor) : IEntityComponent
    {
        public Color BackgroundColor { get; set; } = backgroundColor;

        public override string ToString()
        {
            return $"Background : {BackgroundColor}";
        }
    }
}
