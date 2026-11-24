using Engine.Math;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Game.Views;

namespace Presentation.MapRendering;

/// <summary>The one rule for which tiles show a shadow of something flying above them, read by the map and the Inspection window.</summary>
/// <remarks>Only the Flying layer casts one, and only onto Ground: a Ground tile is shadowed when anything on the Flying layer covers it.</remarks>
public static class FlyingShadows
{
    /// <summary>The layers whose occupants cast a shadow onto mapLayer, as a MapLayer bit mask; 0 for every layer but Ground.</summary>
    public static byte CastingLayerMaskOnto(int mapLayer, int layerCount) =>
        mapLayer == (int)MapLayer.Ground && layerCount > (int)MapLayer.Flying ? (byte)(1 << (int)MapLayer.Flying) : (byte)0;

    /// <summary>The biggest size among what casts a shadow onto (x, y) of mapLayer; false when nothing does.</summary>
    public static bool TryGetLargestCasterSize(IMapViewQuery map, int x, int y, int mapLayer, out EntitySizeCategory largest)
    {
        largest = EntitySizeCategory.Tiny;
        if (CastingLayerMaskOnto(mapLayer, map.Bounds.Depth) == 0)
        {
            return false;
        }

        var found = false;
        foreach (var entityId in map.GetOccupants(new Vector3Int(x, y, (int)MapLayer.Flying)))
        {
            if (!map.TryGetOccupant(entityId, out var occupant))
            {
                continue;
            }

            var size = EntitySizeCategories.Of(occupant.Size, occupant.Kind);
            if (!found || size > largest)
            {
                largest = size;
                found = true;
            }
        }

        return found;
    }

    /// <summary>What the Inspection window says of a shadow cast by something of size.</summary>
    public static string DescriptionFor(EntitySizeCategory size) => size switch
    {
        EntitySizeCategory.Large => "Something large is in the sky above this tile.",
        EntitySizeCategory.Huge => "Something huge is in the sky above this tile.",
        _ => "Something is in the sky above this tile.",
    };
}
