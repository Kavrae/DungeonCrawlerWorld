using Engine.Math;
using Game.Modules.Actions;

namespace Game.Modules.Inventory;

/// <summary>Puts one unit an entity holds down on a tile, by the rule every drop follows.</summary>
/// <remarks>Registered with ToggleItemGroundPlacement by the feature that owns dropping (ItemPiles), so Inventory puts a unit down without knowing what a pile is.</remarks>
public interface IItemGroundPlacer
{
    /// <summary>Drops one unit of holderEntityId's stack stackInstanceId on tile; false, changing nothing, when the drop is refused (out of reach, a wall, a shop there).</summary>
    bool TryPutDownOneUnitAt(int holderEntityId, uint stackInstanceId, Vector3Int tile);
}

/// <summary>Turning a toggle item on in Ground mode: the lit unit is dropped on the aimed tile, so what it holds works from where it lies.</summary>
/// <remarks>
/// A toggle item's lit state is the unit's (ToggleItemHolderSync), and whoever holds a lit unit -- a pile included -- holds
/// its effects, so dropping the lit unit is all Ground mode needs: its aura radiates from the pile. The tile is the
/// selection's aimed tile, aimed as a drop is (the holder's own or one beside it) and put down by the same rule
/// (IItemGroundPlacer, over ItemDrops); Target mode keeps it carried. A toggle with periodic effects is put down too, and
/// its new holder owes its upkeep: a pile can't pay, so it goes out the first time its upkeep comes due
/// (ToggleUpkeepSystem). A refused drop -- the holder moved out of reach during a windup, say -- leaves it lit and carried.
/// Without a placer (a build with no ItemPiles) nothing is ever put down.
/// </remarks>
public sealed class ToggleItemGroundPlacement
{
    private IItemGroundPlacer? _placer;

    /// <summary>Registers what puts a unit down. One placer.</summary>
    public void RegisterPlacer(IItemGroundPlacer placer)
    {
        if (_placer is not null)
        {
            throw new InvalidOperationException("Toggle items already have a ground placer.");
        }

        _placer = placer;
    }

    /// <summary>After litStackInstanceId's unit was just lit by an activation aimed with selection: drops it on the aimed tile when that was in Ground mode.</summary>
    public void PutDownIfGround(int holderEntityId, uint litStackInstanceId, in TargetSelection selection)
    {
        if (selection.Mode != TargetingMode.Ground || _placer is null)
        {
            return;
        }

        _placer.TryPutDownOneUnitAt(holderEntityId, litStackInstanceId, selection.AimedTile);
    }
}
