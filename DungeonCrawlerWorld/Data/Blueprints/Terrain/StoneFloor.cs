using DungeonCrawlerWorld.Components;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Data.Blueprints.Terrain
{
    /// <summary>
    /// Represents a patch of roughly shaped stone floor terrain.
    /// </summary>
    /// <components>
    /// BackgroundComponent
    /// DisplayTextComponent
    /// TransformComponent
    /// </components>
    public class StoneFloor : IBlueprint
    {
        public static void Build(int entityId)
        {
            ComponentRepo.BackgroundComponents.Add(
                entityId,
                new BackgroundComponent(Color.LightGray));

            ComponentRepo.DisplayTextComponents.Add(
                entityId,
                new DisplayTextComponent("Stone floor", "Roughly shaped stone floor."));

            ComponentRepo.TransformComponents.Add(
                entityId,
                new TransformComponent(
                    new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground),
                    new Utilities.Vector3Byte(1, 1, 1)));
        }
    }
}
