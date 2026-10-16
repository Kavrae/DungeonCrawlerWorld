using Game.Modules.Actions;
using Game.Modules.Currency;
using Game.Modules.Inventory;
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
/// mouse position, not relative to any window's own bounds.
/// </summary>
public sealed class DragGhostContent(
    PointerState pointerState,
    World world,
    ActionCatalog actionCatalog,
    ItemCatalog itemCatalog,
    InventoryView inventoryView,
    FontService fontService,
    SpriteSheetService spriteSheetService,
    LabelRenderer labelRenderer) : IElementContent
{
    private Window _hostWindow = null!;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime)
    {
        var state = pointerState.ContentDrag;
        if (!state.Visible || state.Payload is not DragPayload drag)
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
        else if (drag.MergedItemDefinitionId is { } mergedItemDefinitionId && itemCatalog.TryGet(mergedItemDefinitionId, out var mergedItem))
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

        var size = state.SourceSize;
        SpriteView? sprite = spriteName is not null && spriteSheetService.Sprites.TryGetFirst(spriteName, out var spriteView) ? spriteView : null;
        var font = fontService.GetFont((int)(size.Y * GameFontChrome.DragGhostGlyphFontFraction));
        var mousePosition = state.CursorPosition;

        DragGhostRenderer.Draw(
            _hostWindow.ElementPoolService.SpriteBatch, spriteSheetService, labelRenderer, font,
            sprite, glyph, glyphColor, new Vector2(mousePosition.X, mousePosition.Y), size);
    }
}
