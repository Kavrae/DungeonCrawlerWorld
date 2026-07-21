using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// Which map node/layer the player is currently looking at -- pure UI/inspection state with
/// no simulation meaning, shared between MapWindow (the sole writer, via click-to-select and
/// Page Up/Down) and SelectionWindowContent (which reads both to scope the inspector to
/// what's actually on screen). Previously lived on World; moved here because nothing in
/// Engine or Game ever read or wrote it.
/// </summary>
public sealed class MapViewState
{
    /// <summary>2D coordinates of the currently selected map node, if any -- paired with CurrentMapLayer for the Z.</summary>
    public Point? SelectedMapNodePosition { get; set; }

    /// <summary>
    /// The single MapLayer currently displayed/inspected -- shared state between MapWindow
    /// (the only writer, via Page Up/Down) and SelectionWindowContent (which scopes the
    /// inspector to this layer, matching what's actually visible on screen), the same way
    /// SelectedMapNodePosition already coordinates those two windows.
    /// </summary>
    public int CurrentMapLayer { get; set; } = (int)MapLayer.Ground;
}
