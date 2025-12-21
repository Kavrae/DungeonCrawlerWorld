using System;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;

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
        public static void Build( int entityId )
        {
            _ = new DisplayTextComponent(entityId, "Wall", "Basic wall. Default implementation.");
            _ = new GlyphComponent(entityId, "[][]", Color.DarkGray, new Point(0, -1));
            _ = new TransformComponent(entityId, new Utilities.Vector3Int(0, 0, (int)MapHeight.Standing), new Utilities.Vector3Int(1, 1, 1));
        }
    }
}
