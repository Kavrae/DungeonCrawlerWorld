using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Which map node/layer the player is currently looking at -- pure UI/inspection state with
/// no simulation meaning, shared between MapWindow (the sole writer, via click-to-select and
/// Page Up/Down) and SelectionWindowContent (which reads both to scope the inspector to
/// what's actually on screen). Previously lived on World; moved here because nothing in
/// Engine or Game ever read or wrote it. Follows the same *State plain-field convention as
/// ElementGeometryState/ElementHeaderState/ElementBorderState/ElementContentState -- see
/// ElementGeometryState for why.
/// </summary>
public sealed class MapViewState
{
    /// <summary>2D coordinates of the currently selected map node, if any -- paired with CurrentMapLayer for the Z.</summary>
    public Point? SelectedMapNodePosition;

    /// <summary>
    /// The single MapLayer currently displayed/inspected -- shared state between MapWindow
    /// (the only writer, via Page Up/Down) and SelectionWindowContent (which scopes the
    /// inspector to this layer, matching what's actually visible on screen), the same way
    /// SelectedMapNodePosition already coordinates those two windows.
    /// </summary>
    public int CurrentMapLayer = (int)MapLayer.Ground;

    /// <summary>The action currently armed (a hotkey pressed once, awaiting a target), if any -- paired with ArmedSlot so the Hotbar UI (a later phase) can highlight which slot it came from. Cleared on disarm (pressing the same slot again) or, once built, on cancel/activation. Mutually exclusive with ArmedItemStackInstanceId -- only one of {action, item} is ever armed at once.</summary>
    public Guid? ArmedActionId;

    /// <summary>The consumable item stack currently armed, if any -- see ArmedActionId, which this mirrors exactly for the item-hotkey path (Game.Modules.Inventory.Components.ItemHotkeyBindingComponent) instead of the action one. By StackInstanceId, not ItemDefinitionId -- see that component's own doc comment for why.</summary>
    public Guid? ArmedItemStackInstanceId;

    /// <summary>See ArmedActionId/ArmedItemStackInstanceId -- shared regardless of which of the two is actually armed.</summary>
    public HotkeySlot? ArmedSlot;

    /// <summary>The hotbar slot currently under the cursor, once HotbarController's hover tracking
    /// has held on the same bound slot for at least HudChrome.HoverTooltipDelayFrames -- null the
    /// instant the cursor moves off that slot (no delay on hiding, only on showing). Takes priority
    /// over ArmedSlot, since a live hover is the most immediate signal of intent.</summary>
    public HotkeySlot? HoverSlot;

    /// <summary>Every tile the currently-armed action could possibly be aimed at from the caster's current position -- Adjacent's fixed footprint, or every tile within the action's Range for cursor-directed shapes. Computed at arm time and recomputed if the caster moves while still armed (see ActionTargetingController.RefreshTargetableTiles). Null when nothing is armed.</summary>
    public IReadOnlySet<Vector3Int>? TargetableTiles;

    /// <summary>The map tile the mouse is currently over, on the player's own Z layer -- null when nothing is armed or the mouse isn't over the map. Updated every frame while an action is armed (see MapWindow.UpdateHoveredTile).</summary>
    public Vector3Int? HoveredTile;

    /// <summary>What InspectionWindow is currently showing, if anything -- see InspectionWindow's own doc comment.</summary>
    public InspectionMode InspectionMode;

    /// <summary>Detail mode's followed target -- -1 when none (Basic mode uses SelectedMapNodePosition instead, since it targets a tile, not a single followed entity).</summary>
    public int InspectedEntityId = -1;

    /// <summary>The inventory item stack currently shown in the Item Details window, if any -- drives the selection glow on both InventoryGridContent's matching cell and HotbarContent's matching bound slot (see GlowRenderer.Draw, the same primitive ArmedSlot's own glow already uses). By StackInstanceId, not ItemDefinitionId -- same reasoning as ArmedItemStackInstanceId above. Set/cleared by ItemDetailsWindowController.Open/Close.</summary>
    public Guid? SelectedItemStackInstanceId;

    /// <summary>Non-null while Item Details Comparison is armed (see ItemComparisonController.Arm/Disarm/ClearComparison) -- the anchor item's own Activator concrete type, the eligibility gate every other item must match to be added. InventoryGridContent reads this every frame to grey out ineligible cells and highlight eligible ones (see InventoryItemStackCell.CompareState).</summary>
    public Type? CompareRequiredActivatorType;

    /// <summary>The currently-open shop's own entity id, if any -- set/cleared by ShopWindowController.OpenShop/its own Closed handler, the same shared cross-window flag CompareRequiredActivatorType above already is. Not yet read by anything (a future pass switches both the shop's own grid and the player's own inventory grid to the wider, price-showing ShopItemStackCell layout while this is set).</summary>
    public int? OpenShopEntityId;
}

/// <summary>
/// InspectionWindow's current tier -- Basic (click a tile, see SelectedMapNodePosition), Detail
/// (context-menu Inspect while GlobalState.IsAdminModeOn is off, follows InspectedEntityId), or
/// Admin (the same context-menu Inspect while GlobalState.IsAdminModeOn is on -- see
/// MapWindow.InspectEntity). Detail and Admin are (temporarily) rendered identically by
/// InspectionWindowContent -- Admin's full raw-component dump is appended beneath both today,
/// same as before this split existed -- but they're now distinct enum values so a future pass can
/// gate the dump behind Admin alone instead.
/// </summary>
public enum InspectionMode : byte
{
    None,
    Basic,
    Detail,
    Admin,
}