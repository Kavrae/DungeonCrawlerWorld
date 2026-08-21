using Engine.ECS.Components.Stores;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// The live content-drag state DragGhostContent needs to draw a frame -- see DragGhostContent.GetState.
/// Bundles what UiInputController's own content-drag fields expose (ContentDragGhostVisible,
/// ContentDragItemStackInstanceId, ContentDragMergedItemDefinitionId, ContentDragActionId,
/// ContentDragOriginEntityId, ContentDragSourceSize, CurrentMousePosition) into one snapshot
/// instead of DragGhostContent holding a live UiInputController reference just to pull seven
/// unrelated properties off it every frame. MergedItemDefinitionId is the icon fallback for a
/// Merged Stack drag (see InventoryItemStackCell's own doc comment for the Base/Diverging/Merged
/// vocabulary) -- it has no single StackInstanceId to resolve an icon through, only its own
/// shared ItemDefinitionId. OriginEntityId is null for a hotbar-origin drag (always the player's
/// own item/action either way) -- only an InventoryItemStackCell-origin drag sets it, and it may
/// belong to any entity's own inventory, not just the player's.
/// </summary>
public readonly record struct DragGhostState(bool Visible, Guid? ItemStackInstanceId, Guid? MergedItemDefinitionId, Guid? ActionId, int? OriginEntityId, Vector2 SourceSize, Point CursorPosition);

/// <summary>
/// A cursor-following copy of a dragged item's or action's icon while UiInputController's
/// content-drag (inventory cell/bound hotbar slot &lt;-&gt; hotbar slot, see its own doc comment)
/// is in progress -- purely visual feedback, no gameplay state of its own. Sized to the dragged
/// element's own on-screen size (see DragGhostState.SourceSize) -- rather than one fixed size for
/// every drag, so the ghost doesn't visibly jump in scale relative to wherever it was picked up
/// from. Hosted in a minimal (zero-size, fully transparent) User-tier Window -- see
/// ShellBootstrapper.Build -- since everything this draws is positioned directly at the live
/// mouse position, not relative to any window's own bounds.
/// </summary>
public sealed class DragGhostContent(
    World world,
    ActionCatalog actionCatalog,
    ItemCatalog itemCatalog,
    MultiComponentPool<InventoryItemStackComponent> inventoryStacks,
    FontService fontService,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    GlyphRenderer glyphRenderer) : IElementContent
{
    private const float GlyphSizeFraction = 0.6f;

    /// <summary>
    /// How DrawContent finds the live content-drag state -- assigned once ShellBootstrapper.Build
    /// has constructed a real UiInputController, which happens after this class does (see that
    /// method's own comment on why). Defaults to a not-visible state so an unwired instance (e.g.
    /// in a test) never null-refs and simply never draws.
    /// </summary>
    public Func<DragGhostState> GetState { get; set; } = static () => default;

    private Window _hostWindow = null!;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime)
    {
        var state = GetState();
        if (!state.Visible)
        {
            return;
        }

        string? spriteName;
        string glyph;
        Color glyphColor;

        if (state.ItemStackInstanceId is { } stackInstanceId &&
            InventoryQueries.TryFindByStackInstanceId(inventoryStacks, state.OriginEntityId ?? world.PlayerEntityId, stackInstanceId, out var stack) &&
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
        else
        {
            return;
        }

        var size = state.SourceSize;
        SpriteComponent? sprite = spriteName is not null && SpriteManifest.TryGet(spriteName, out var spriteComponent) ? spriteComponent : null;
        var font = fontService.GetFont((int)(size.Y * GlyphSizeFraction));
        var mousePosition = state.CursorPosition;

        DragGhostRenderer.Draw(
            _hostWindow.ElementPoolService.SpriteBatch, spriteSheetService, spriteRenderer, glyphRenderer, font,
            sprite, glyph, glyphColor, new Vector2(mousePosition.X, mousePosition.Y), size);
    }
}
