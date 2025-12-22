using DungeonCrawlerWorld.Components;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Data.Blueprints.Terrain
{
    /// <summary>
    /// Represents a patch of ordinary dirt terrain.
    /// </summary>
    /// <components>
    /// BackgroundComponent
    /// DisplayTextComponent
    /// TransformComponent
    /// </components>
    public class Dirt : IBlueprint
    {
        public static void Build(int entityId)
        {
            ComponentRepo.SaveBackgroundComponent(
                entityId,
                new BackgroundComponent(Color.Tan),
                ComponentSaveMode.Overwrite);

            ComponentRepo.SaveDisplayTextComponent(
                entityId,
                new DisplayTextComponent("Dirt", "Ordinary dirt. Nothing special."),
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
