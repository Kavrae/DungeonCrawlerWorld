using DungeonCrawlerWorld.Components;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Data.Blueprints.Terrain
{
    /// <summary>
    /// Represents a patch of ordinary grass terrain.
    /// </summary>
    /// <components>
    /// BackgroundComponent
    /// DisplayTextComponent
    /// GlyphComponent
    /// TransformComponent
    /// </components>
    public class Grass : IBlueprint
    {
        public static void Build(int entityId)
        {
            ComponentRepo.SaveBackgroundComponent(
                entityId,
                new BackgroundComponent(Color.ForestGreen),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveDisplayTextComponent(
                entityId,
                new DisplayTextComponent("Grass", "Ordinary grass. Nothing special."),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveGlyphComponent(
                entityId,
                new GlyphComponent(",", Color.LawnGreen, new Point(5, -2)),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveTransformComponent(
                entityId,
                 new TransformComponent(
                     new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground),
                     new Utilities.Vector3Byte(1, 1, 1)),
                 ComponentSaveMode.Overwrite);
        }
    }
}
