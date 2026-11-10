using Game.Modules.Actions;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Modules.ItemPiles;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;

using Presentation.Input.DragDrop;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>
/// A cursor-following copy of a dragged item's or action's icon while a content drag
/// carrying a DragPayload is in progress (see PointerState.ContentDrag) -- purely visual feedback, no gameplay state of its own. Sized to the dragged
/// element's own on-screen size (see ContentDragState.SourceSize) -- rather than one fixed size for
/// every drag, so the ghost doesn't visibly jump in scale relative to wherever it was picked up
/// from. Hosted in a minimal (zero-size, fully transparent) User-tier Window -- see
/// ShellBootstrapper.BuildUserWindows -- since everything this draws is positioned directly at the live
/// mouse position, not relative to any window's own bounds. A drop aimed on the map (ActionTargetingController.AimedDrop: armed from a menu, or a toggle item armed in Ground mode) carries the
/// same ghost, at the size of a hotbar slot, so it looks exactly like dragging what it drops.
/// </summary>
public sealed class DragGhostContent(
    PointerState pointerState,
    ActionTargetingController actionTargeting,
    World world,
    ActionCatalog actionCatalog,
    ItemCatalog itemCatalog,
    InventoryView inventoryView,
    FontService fontService,
    SpriteSheetService spriteSheetService) : IElementContent
{
    private Window _hostWindow = null!;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime)
    {
        var state = pointerState.ContentDrag;
        DragPayload drag;
        Vector2 size;
        Point mousePosition;
        if (state.Visible && state.Payload is DragPayload heldDrag)
        {
            (drag, size, mousePosition) = (heldDrag, state.SourceSize, state.CursorPosition);
        }
        else if (actionTargeting.AimedDrop is { } armedDrop)
        {
            (drag, size, mousePosition) = (PayloadOf(armedDrop), HotbarContent.SlotSize, pointerState.CursorPosition);
        }
        else
        {
            return;
        }

        string? spriteName;
        string glyph;
        Color glyphColor;

        if (drag.ItemStackInstanceId is { } stackInstanceId &&
            inventoryView.TryGetStack(drag.OriginEntityId ?? world.PlayerEntityId, stackInstanceId, out var stack) &&
            InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item))
        {
            (spriteName, glyph, glyphColor) = (item.SpriteName, item.Glyph, item.GlyphColor);
        }
        else if (drag.MergedItemDefinitionId is { } mergedItemDefinitionId && inventoryView.TryGetItem(drag.OriginEntityId ?? world.PlayerEntityId, mergedItemDefinitionId, out var mergedItem))
        {
            (spriteName, glyph, glyphColor) = (mergedItem.SpriteName, mergedItem.Glyph, mergedItem.GlyphColor);
        }
        else if (drag.ActionId is { } actionId && actionCatalog.TryGet(actionId, out var action))
        {
            (spriteName, glyph, glyphColor) = (action.SpriteName, action.Glyph, action.GlyphColor);
        }
        else if (drag.CurrencyType is { } currencyType)
        {
            (spriteName, glyph, glyphColor) = currencyType switch
            {
                CurrencyType.Gold => ("Currency-Gold", "G", Color.Gold),
                CurrencyType.Credits => ("Currency-Credit", "C", Color.LightBlue),
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
        else
        {
            return;
        }

        SpriteView? sprite = spriteName is not null && spriteSheetService.Sprites.TryGetFirst(spriteName, out var spriteView) ? spriteView : null;
        var glyphFontMeasurer = fontService.GetTextMeasurer(fontService.GetFont((int)(size.Y * GameFontChrome.DragGhostGlyphFontFraction)));

        DragGhostRenderer.Draw(
            _hostWindow.Render.Batch, spriteSheetService, glyphFontMeasurer,
            sprite, glyph, glyphColor, _hostWindow.ToContentLocal(new Vector2(mousePosition.X, mousePosition.Y)), size);
    }

    /// <summary>What a drag of the player's own goods carrying drop would hold: the stack, the Merged Stack's item, or the currency (Gold for all of it).</summary>
    private DragPayload PayloadOf(ItemDrop drop) => drop.Kind switch
    {
        ItemDropKind.OneUnit or ItemDropKind.WholeStack => new(DragPayloadOrigin.InventoryCell, ItemStackInstanceId: drop.StackInstanceId, OriginEntityId: world.PlayerEntityId),
        ItemDropKind.EveryStackOfItem => new(DragPayloadOrigin.InventoryCell, MergedItemDefinitionId: drop.ItemDefinitionId, OriginEntityId: world.PlayerEntityId),
        ItemDropKind.Currency => new(DragPayloadOrigin.Currency, CurrencyType: drop.CurrencyType, OriginEntityId: world.PlayerEntityId),
        ItemDropKind.AllCurrency => new(DragPayloadOrigin.Currency, CurrencyType: CurrencyType.Gold, OriginEntityId: world.PlayerEntityId),
        _ => throw new ArgumentOutOfRangeException(nameof(drop), drop.Kind, null),
    };
}
