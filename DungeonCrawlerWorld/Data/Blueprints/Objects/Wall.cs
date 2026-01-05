using DungeonCrawlerWorld.Components;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Data.Blueprints.Objects
{
    /// <summary>
    /// Represents a basic wall object.
    /// </summary>
    /// <components>
    /// DisplayTextComponent
    /// GlyphComponent
    /// TransformComponent
    /// </components>
    public class Wall : IBlueprint
    {
        public static void Build(int entityId)
        {
            ComponentRepo.DisplayTextComponents.Add(
                entityId,
                new DisplayTextComponent("Wall", "Basic wall. Default implementation."));

            ComponentRepo.GlyphComponents.Add(
                entityId,
                new GlyphComponent("[][]", Color.DarkGray, new Point(0, -1)));

            ComponentRepo.TransformComponents.Add(
                entityId,
                new TransformComponent(
                    new Utilities.Vector3Int(0, 0, (int)MapHeight.Standing),
                    new Utilities.Vector3Byte(1, 1, 1)));
        }
    }
}
