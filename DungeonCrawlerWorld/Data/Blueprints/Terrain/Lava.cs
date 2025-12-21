using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

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
       public static void Build( int entityId )
        {
            _ = new GlyphComponent(entityId, "~", Color.Yellow, new Point(3, 0));
            _ = new BackgroundComponent(entityId, Color.OrangeRed);
            _ = new DisplayTextComponent(entityId, "Lava", "Hot lava. I do not recommend stepping on it.");
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Ground), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
