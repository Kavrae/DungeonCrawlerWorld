using Engine.Math;
using Game.Modules.ItemPiles;
using Game.Views;
using Presentation.UI.Inventory;

namespace Presentation.UI;

/// <summary>What an armed drop is over: a container it would go into reads "Add" at the cursor and is lit green, or red where it would refuse; a standing shop is always red.</summary>
/// <remarks>
/// The armed drop's side of what a drag over a container shows (MapDropHandler), so a menu Drop and a drag look the same before
/// they land. Driven by ActionTargetingController.ArmedDropHovered every update while a drop is armed, held or not; it clears
/// only the highlight it set itself, so it never takes away a drag's.
/// </remarks>
public sealed class ArmedDropHover
{
    private readonly IMapViewQuery _mapView;
    private readonly MapViewState _mapViewState;
    private readonly IPlayerItemDrops _playerItemDrops;
    private readonly DropCursorText _dropCursorText;
    private readonly ActionTargetingController _actionTargeting;
    private bool _setsHighlight;

    public ArmedDropHover(IMapViewQuery mapView, MapViewState mapViewState, IPlayerItemDrops playerItemDrops, DropCursorText dropCursorText, ActionTargetingController actionTargeting)
    {
        _mapView = mapView;
        _mapViewState = mapViewState;
        _playerItemDrops = playerItemDrops;
        _dropCursorText = dropCursorText;
        _actionTargeting = actionTargeting;
        actionTargeting.ArmedDropHovered += OnArmedDropHovered;
        actionTargeting.ArmedDropChanged += OnArmedDropChanged;
    }

    private void OnArmedDropHovered(Vector3Int? hoveredTile)
    {
        if (_actionTargeting.AimedDrop is not { } drop || hoveredTile is not { } tile)
        {
            ClearOverContainer();
            return;
        }

        var receiver = _mapView.FindDropReceiver(_mapView.GetOccupants(tile), out var containerEntityId);
        if (receiver == DropReceiverKind.Ground)
        {
            ClearOverContainer();
            return;
        }

        var isShop = receiver == DropReceiverKind.Shop;
        _mapViewState.ContainerDropHighlight = (containerEntityId, !isShop && _playerItemDrops.CanDropAt(drop, tile));
        _setsHighlight = true;
        _dropCursorText.SetArmedDropOverContainer(!isShop);
    }

    private void ClearOverContainer()
    {
        ClearOwnHighlight();
        _dropCursorText.SetArmedDropOverContainer(false);
    }

    private void OnArmedDropChanged()
    {
        if (_actionTargeting.AimedDrop is null)
        {
            ClearOwnHighlight();
        }
    }

    private void ClearOwnHighlight()
    {
        if (!_setsHighlight)
        {
            return;
        }

        _mapViewState.ContainerDropHighlight = null;
        _setsHighlight = false;
    }
}
