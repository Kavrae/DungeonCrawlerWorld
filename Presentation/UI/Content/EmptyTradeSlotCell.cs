using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Pure decoration filling an unused slot in one of the trade window's own fixed-size grids
/// (InventoryGridContent.RebuildCells, only when tradeGridIsShopSide is set) -- signals "you can
/// still drop something here" without pretending to be a real, interactable cell. Never added to
/// InventoryGridContent's own _cells list (hover/selection/compare-state sync all iterate that
/// list, not this window's full ChildElements), and IsHitTestable is unconditionally false rather
/// than the usual _isVisible default, so it can never be hovered, clicked, dragged, or
/// right-clicked regardless of what UiInputController's hit-test does. DrawContent is a single
/// glow call, nothing else -- created with ShowBorder false and ContentColor Transparent (the same
/// options every real cell already uses), so there is no border or background to suppress either.
/// </summary>
public sealed class EmptyTradeSlotCell(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
    : Element(fontService, elementPoolService, labelRenderer)
{
    protected override bool IsHitTestable => false;

    public override void DrawContent(GameTime gameTime)
    {
        var bounds = new Rectangle((int)ContentAbsolutePosition.X, (int)ContentAbsolutePosition.Y, (int)ContentSize.X, (int)ContentSize.Y);
        GlowRenderer.Draw(ElementPoolService.SpriteBatch, ElementPoolService.UnitRectangle, bounds, Color.White, GlowMode.InteriorFade);
    }
}
