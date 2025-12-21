using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

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
       public static void Build( int entityId )
        {
            _ = new BackgroundComponent(entityId, Color.Tan);
            _ = new DisplayTextComponent(entityId, "Dirt", "Ordinary dirt. Nothing special.");
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
