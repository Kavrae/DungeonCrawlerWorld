using Engine.Math;
using Game.Modules.Abilities;
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

    /// <summary>The ability currently armed (a hotkey pressed once, awaiting a target), if any -- paired with ArmedSlot so the Hotbar UI (a later phase) can highlight which slot it came from. Cleared on disarm (pressing the same slot again) or, once built, on cancel/activation. Mutually exclusive with ArmedItemDefinitionId -- only one of {ability, item} is ever armed at once.</summary>
    public Guid? ArmedAbilityId;

    /// <summary>The consumable item currently armed, if any -- see ArmedAbilityId, which this mirrors exactly for the item-hotkey path (Game.Modules.Inventory.Components.ItemHotkeyBindingComponent) instead of the ability one.</summary>
    public Guid? ArmedItemDefinitionId;

    /// <summary>See ArmedAbilityId/ArmedItemDefinitionId -- shared regardless of which of the two is actually armed.</summary>
    public HotkeySlot? ArmedSlot;

    /// <summary>The hotbar slot currently under the cursor, once HotbarController's hover tracking
    /// has held on the same bound slot for at least HudMetrics.HoverTooltipDelayFrames -- null the
    /// instant the cursor moves off that slot (no delay on hiding, only on showing). Takes priority
    /// over ArmedSlot, since a live hover is the most immediate signal of intent.</summary>
    public HotkeySlot? HoverSlot;

    /// <summary>Every tile the currently-armed ability could possibly be aimed at from the caster's current position -- Adjacent's fixed footprint, or every tile within the ability's Range for cursor-directed shapes. Computed at arm time and recomputed if the caster moves while still armed (see AbilityTargetingController.RefreshTargetableTiles). Null when nothing is armed.</summary>
    public IReadOnlySet<Vector3Int>? TargetableTiles;

    /// <summary>The map tile the mouse is currently over, on the player's own Z layer -- null when nothing is armed or the mouse isn't over the map. Updated every frame while an ability is armed (see MapWindow.UpdateHoveredTile).</summary>
    public Vector3Int? HoveredTile;
}