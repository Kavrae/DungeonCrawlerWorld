using Engine.Math;
using Game.Modules.ItemPiles;

namespace Presentation.UI.Inventory;

/// <summary>Putting the player's goods down on a tile at once: what a confirmed armed drop and a drag released on the map both do.</summary>
public interface IItemDropPlacement
{
    /// <summary>Whether drop can be put down on tile now: into a container there, or onto the ground in reach.</summary>
    bool CanDropAt(ItemDrop drop, Vector3Int tile);

    /// <summary>Puts drop down on tile at once. Returns false, changing nothing, when it can't go there.</summary>
    bool TryDropAt(ItemDrop drop, Vector3Int tile);
}
