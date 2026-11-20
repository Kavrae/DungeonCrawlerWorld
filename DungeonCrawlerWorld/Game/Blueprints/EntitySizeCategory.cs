using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Blueprints;

/// <summary>How big an entity is, by name: what a player is told about its size.</summary>
public enum EntitySizeCategory : byte
{
    Tiny,
    Normal,
    Large,
    Huge,
}

/// <summary>The one rule naming an entity's size from its footprint and occupancy.</summary>
public static class EntitySizeCategories
{
    /// <summary>Tiny when it is Tiny whatever its footprint; otherwise by the footprint's largest dimension: 1 Normal, 2 Large, 3 or more Huge.</summary>
    public static EntitySizeCategory Of(Vector2Byte footprint, NonBlockingKind kind)
    {
        if ((kind & NonBlockingKind.Tiny) != 0)
        {
            return EntitySizeCategory.Tiny;
        }

        return System.Math.Max(footprint.X, footprint.Y) switch
        {
            <= 1 => EntitySizeCategory.Normal,
            2 => EntitySizeCategory.Large,
            _ => EntitySizeCategory.Huge,
        };
    }
}
