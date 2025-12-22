using DungeonCrawlerWorld.Components;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Data.Blueprints.Terrain
{
    /// <summary>
    /// Represents a patch of hot lava terrain.
    /// </summary>
    /// <components>
    /// BackgroundComponent
    /// GlyphComponent
    /// DisplayTextComponent
    /// TransformComponent
    /// </components>
    public class Lava : IBlueprint
    {
        //TODO spawn lava in pools around the map
        //TODO stepping on lava deals damage every 10 frames + inflicts a stack of burn (up to x stacks), dealing damage every 10 frames for y triggers, resetting each time more fire is inflicted
        //TODO burn icon on selection
        public static void Build(int entityId)
        {
            ComponentRepo.SaveBackgroundComponent(
                entityId,
                new BackgroundComponent(Color.OrangeRed),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveDisplayTextComponent(
                entityId,
                new DisplayTextComponent("Lava", "Hot lava. I do not recommend stepping on it."),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveGlyphComponent(
                entityId,
                new GlyphComponent("~", Color.Yellow, new Point(3, 0)),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveTransformComponent(
                entityId,
                new TransformComponent(
                    new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground),
                    new Utilities.Vector3Int(1, 1, 1)),
                ComponentSaveMode.Overwrite);
        }
    }
}
