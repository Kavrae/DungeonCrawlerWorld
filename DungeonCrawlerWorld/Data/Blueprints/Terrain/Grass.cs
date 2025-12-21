using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

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
       public static void Build( int entityId )
        {
            _ = new GlyphComponent(entityId, ",", Color.LawnGreen, new Point(5, -2));
            _ = new BackgroundComponent(entityId, Color.ForestGreen);
            _ = new DisplayTextComponent(entityId, "Grass", "Ordinary grass. Nothing special.");
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
