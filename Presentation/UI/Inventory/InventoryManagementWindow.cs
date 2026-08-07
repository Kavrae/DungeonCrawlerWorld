using Engine.ECS.Components;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>
/// The player-facing inventory view: a TabbedContent (today just one "All" tab) showing
/// InventoryGridContent. Close-only (no minimize) -- created fresh by InventoryFolderController
/// each time the Inventory folder is opened and returned to ElementPoolService's pool on close,
/// mirroring NotificationCenter's active-notification-popup lifecycle rather than staying a
/// permanently-existing hidden window. A dedicated Window subclass (rather than a plain Window
/// hosting TabbedContent via SetContent) purely so it can override the inherited
/// OnContentClickAction and forward tab-header clicks to TabbedContent.HandleClick -- the
/// codebase's own convention for when a Window subclass is warranted (MapWindow, TextWindow).
/// </summary>
public sealed class InventoryManagementWindow(
    FontService fontService,
    ElementPoolService elementPoolService,
    GlyphRenderer glyphRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    ComponentManager componentManager,
    ItemCatalog itemCatalog) : Window(fontService, elementPoolService, glyphRenderer)
{
    /// <summary>Dark grey background for both this window's own content area and TabbedContent's body window (see Configure) -- individual grid cells stay white-with-a-black-border (InventoryItemStackCell) so they read as distinct squares against it. Shared with AbilityScoreWindow's own background -- see WindowPalette.</summary>
    public static readonly Color BackgroundColor = WindowPalette.PanelBackgroundColor;

    private TabbedContent _tabbedContent = null!;

    /// <summary>Builds this window's content for entityId's inventory. Must be called after CreateElement but before Initialize (see Window.SetContent's own doc comment) -- a fresh TabbedContent/InventoryGridContent per open, since entityId varies across opens of a pooled/reused window instance.</summary>
    public void Configure(int entityId)
    {
        // Detach the previous open's TabbedContent (if this window instance is being reused from
        // the pool) before discarding it -- see TabbedContent.Detach's own doc comment.
        _tabbedContent?.Detach();

        var gridContent = new InventoryGridContent(componentManager, itemCatalog, elementPoolService, fontService, glyphRenderer, spriteSheetService, spriteRenderer, entityId);
        _tabbedContent = new TabbedContent([new TabbedContent.TabDefinition("All", gridContent)], elementPoolService, fontService, glyphRenderer, BackgroundColor);
        SetContent(_tabbedContent);
    }

    protected override void OnContentClickAction(Point mousePosition)
    {
        base.OnContentClickAction(mousePosition);
        _tabbedContent.HandleClick(mousePosition);
    }
}
