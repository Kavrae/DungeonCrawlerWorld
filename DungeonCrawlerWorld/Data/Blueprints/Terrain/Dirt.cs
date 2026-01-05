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
            ComponentRepo.BackgroundComponents.Add(
                entityId,
                new BackgroundComponent(Color.Tan));

            ComponentRepo.DisplayTextComponents.Add(
                entityId,
                new DisplayTextComponent("Dirt", "Ordinary dirt. Nothing special."));

            ComponentRepo.TransformComponents.Add(
                entityId,
                new TransformComponent(
                    new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground),
                    new Utilities.Vector3Byte(1, 1, 1)));
        }
    }
}
