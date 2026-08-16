using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Content;

namespace Presentation.UI;

/// <summary>
/// Shows the name/summary (see ActionDefinition/ItemDefinition's Summary vs Description doc
/// comments -- this window uses Summary, the concise one) of whichever hotbar slot is currently
/// armed or hovered (see HotbarController, the only writer of MapViewState.ArmedSlot/HoverSlot
/// relevant here). A HoverPopupWindow specialization: North anchor (centered above the slot),
/// UseFixedWidth (pinned to HotbarContent.SummaryWidth, only height auto-grows).
/// </summary>
public sealed class ArmedHotkeySummaryWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    GlyphRenderer glyphRenderer,
    MapViewState mapViewState,
    HotbarContent hotbarContent)
    : HoverPopupWindow(fontService, elementPoolService, glyphRenderer)
{
    /// <summary>Bottom edge sits exactly this far above the summarized slot's top edge.</summary>
    private static readonly Vector2 Gap = new(0, 1);

    private HotkeySlot? _displayedSlot;

    protected override bool UseFixedWidth => true;

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        var slotToShow = mapViewState.HoverSlot ?? mapViewState.ArmedSlot;

        if (slotToShow == _displayedSlot)
        {
            return;
        }

        _displayedSlot = slotToShow;

        if (slotToShow is not { } slot || !hotbarContent.TryGetSlotSummary(slot, out var title, out var summary))
        {
            Hide();
            return;
        }

        ShowNear(hotbarContent.GetSlotBounds(slot), PopupAnchor.North, Gap, summary, title);
    }
}
