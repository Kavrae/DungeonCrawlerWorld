using Game.Blueprints;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// A cursor-following copy of a dragged item's icon while GameInputController's content-drag
/// (inventory cell &lt;-&gt; hotbar slot, see its own doc comment) is in progress -- purely
/// visual feedback, no gameplay state of its own. Sized to GameInputController.ContentDragSourceSize
/// -- the actual size of whatever element the drag started on -- rather than one fixed size for
/// every drag, so the ghost doesn't visibly jump in scale relative to wherever it was picked up
/// from. Hosted in a minimal (zero-size, fully transparent) User-tier Window -- see
/// GameShellBootstrapper.Build -- since everything this draws is positioned directly at the live
/// mouse position, not relative to any window's own bounds.
/// </summary>
public sealed class DragGhostContent(
    GameInputController inputController,
    ItemCatalog itemCatalog,
    FontService fontService,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    GlyphRenderer glyphRenderer) : IElementContent
{
    private const float GlyphSizeFraction = 0.6f;

    public void Initialize(Window hostWindow) { }

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        if (inputController.ContentDragItemDefinitionId is not { } itemDefinitionId || !itemCatalog.TryGet(itemDefinitionId, out var item))
        {
            return;
        }

        var size = inputController.ContentDragSourceSize;
        SpriteComponent? sprite = item.SpriteName is not null && SpriteManifest.TryGet(item.SpriteName, out var spriteComponent) ? spriteComponent : null;
        var font = fontService.GetFont((int)(size.Y * GlyphSizeFraction));
        var mousePosition = inputController.CurrentMousePosition;

        DragGhostRenderer.Draw(
            spriteBatch, spriteSheetService, spriteRenderer, glyphRenderer, font,
            sprite, item.Glyph, item.GlyphColor, new Vector2(mousePosition.X, mousePosition.Y), size);
    }
}
