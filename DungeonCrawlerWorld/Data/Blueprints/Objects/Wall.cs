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
            ComponentRepo.SaveDisplayTextComponent(
                entityId,
                new DisplayTextComponent("Wall", "Basic wall. Default implementation."),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveGlyphComponent(
                entityId,
                new GlyphComponent("[][]", Color.DarkGray, new Point(0, -1)),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveTransformComponent(
                entityId,
                new TransformComponent(
                    new Utilities.Vector3Int(0, 0, (int)MapHeight.Standing),
                    new Utilities.Vector3Byte(1, 1, 1)),
                ComponentSaveMode.Overwrite);
        }
    }
}
