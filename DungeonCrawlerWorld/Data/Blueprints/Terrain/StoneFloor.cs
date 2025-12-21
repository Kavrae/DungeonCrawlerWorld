using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

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
       public static void Build( int entityId )
        {
            _ = new BackgroundComponent(entityId, Color.LightGray);
            _ = new DisplayTextComponent(entityId, "Stone floor", "Roughly shaped stone floor.");
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
