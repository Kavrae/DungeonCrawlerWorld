using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Game.Modules.Inventory;
using Game.Sprites;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI.Chrome;

namespace Presentation.UI.Content;

/// <summary>
/// The live content-drag state DragGhostContent needs to draw a frame -- see PointerState.ContentDrag.
/// Bundles what UiInputController's own content-drag fields expose (ContentDragGhostVisible,
/// ContentDragItemStackInstanceId, ContentDragMergedItemDefinitionId, ContentDragActionId,
/// ContentDragCurrencyType, ContentDragOriginEntityId, ContentDragSourceSize, CurrentMousePosition) into one snapshot
/// instead of DragGhostContent holding a live UiInputController reference just to pull seven
/// unrelated properties off it every frame. MergedItemDefinitionId is the icon fallback for a
/// Merged Stack drag (see InventoryItemStackCell's own doc comment for the Base/Diverging/Merged
/// vocabulary) -- it has no single StackInstanceId to resolve an icon through, only its own
/// shared ItemDefinitionId. OriginEntityId is null for a hotbar-origin drag (always the player's
/// own item/action either way) -- only an InventoryItemStackCell-origin drag sets it, and it may
/// belong to any entity's own inventory, not just the player's.
/// </summary>
public readonly record struct DragGhostState(bool Visible, uint? ItemStackInstanceId, Guid? MergedItemDefinitionId, Guid? ActionId, CurrencyType? CurrencyType, int? OriginEntityId, Vector2 SourceSize, Point CursorPosition);

/// <summary>
/// A cursor-following copy of a dragged item's or action's icon while UiInputController's
/// content-drag (inventory cell/bound hotbar slot &lt;-&gt; hotbar slot, see its own doc comment)
/// is in progress -- purely visual feedback, no gameplay state of its own. Sized to the dragged
/// element's own on-screen size (see DragGhostState.SourceSize) -- rather than one fixed size for
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
    SpriteRenderer spriteRenderer,
    LabelRenderer labelRenderer) : IElementContent
{
    private Window _hostWindow = null!;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime)
    {
        var state = pointerState.ContentDrag;
        if (!state.Visible)
        {
            return;
        }

        string? spriteName;
        string glyph;
        Color glyphColor;

        if (state.ItemStackInstanceId is { } stackInstanceId &&
            inventoryView.TryGetStack(state.OriginEntityId ?? world.PlayerEntityId, stackInstanceId, out var stack) &&
            InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out var item))
        {
            (spriteName, glyph, glyphColor) = (item.SpriteName, item.Glyph, item.GlyphColor);
        }
        else if (state.MergedItemDefinitionId is { } mergedItemDefinitionId && itemCatalog.TryGet(mergedItemDefinitionId, out var mergedItem))
        {
            (spriteName, glyph, glyphColor) = (mergedItem.SpriteName, mergedItem.Glyph, mergedItem.GlyphColor);
        }
        else if (state.ActionId is { } actionId && actionCatalog.TryGet(actionId, out var action))
        {
            (spriteName, glyph, glyphColor) = (action.SpriteName, action.Glyph, action.GlyphColor);
        }
        else if (state.CurrencyType is { } currencyType)
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
        SpriteComponent? sprite = spriteName is not null && SpriteManifest.TryGetFirst(spriteName, out var spriteComponent) ? spriteComponent : null;
        var font = fontService.GetFont((int)(size.Y * FontChrome.DragGhostGlyphFontFraction));
        var mousePosition = state.CursorPosition;

        DragGhostRenderer.Draw(
            _hostWindow.ElementPoolService.SpriteBatch, spriteSheetService, spriteRenderer, labelRenderer, font,
            sprite, glyph, glyphColor, new Vector2(mousePosition.X, mousePosition.Y), size);
    }
}
