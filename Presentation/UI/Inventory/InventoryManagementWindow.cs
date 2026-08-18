using Engine.ECS.Components;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;

namespace Presentation.UI.Inventory;

/// <summary>
/// The player-facing inventory view: a TabbedContent showing InventoryTabContent (GridControl's
/// count/sort/hide-disabled/search row above an InventoryGridContent), one tab per tag currently
/// carried by the entity's inventory (plus a leading "All" tab) -- see
/// InventoryTagQueries.GetTagCounts. Re-derives the tab list whenever the entity's inventory
/// version changes (item picked up/consumed/etc.), not just once at Configure -- a tag gaining
/// or losing its last carrier should add/remove its tab live while the window is open. Close-only
/// (no minimize) -- created fresh by InventoryFolderController each time the Inventory folder is
/// opened and returned to ElementPoolService's pool on close, mirroring NotificationCenter's
/// active-notification-popup lifecycle rather than staying a permanently-existing hidden window.
/// A dedicated Window subclass (rather than a plain Window hosting TabbedContent via SetContent)
/// purely so Configure/Update have somewhere to live -- the codebase's own convention for when a
/// Window subclass is warranted (MapWindow, TextWindow).
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

    private int _entityId;
    private HoverPopupWindow _hoverPopup = null!;
    private readonly VersionWatcher _tagVersionWatcher = new();

    /// <summary>Builds this window's content for entityId's inventory. Must be called after CreateElement but before Initialize (see Window.SetContent's own doc comment) -- a fresh TabbedContent per open, since entityId varies across opens of a pooled/reused window instance. hoverPopup is owned by InventoryFolderController (created once, top-level, shared across opens) rather than a child of this window -- see HoverPopupWindow's own doc comment for why a nested child can't work here.</summary>
    public void Configure(int entityId, HoverPopupWindow hoverPopup)
    {
        // Detach the previous open's TabbedContent (if this window instance is being reused from
        // the pool) before discarding it -- see TabbedContent.Detach's own doc comment.
        _tabbedContent?.Detach();

        _entityId = entityId;
        _hoverPopup = hoverPopup;

        var tagCounts = InventoryTagQueries.GetTagCounts(componentManager, itemCatalog, entityId);
        _tabbedContent = new TabbedContent(BuildTabDefinitions(tagCounts), elementPoolService, fontService, glyphRenderer, BackgroundColor);
        SetContent(_tabbedContent);

        _tagVersionWatcher.HasChanged(CurrentInventoryVersion()); // Primes the baseline so the next Update doesn't immediately rebuild against the list just built above.
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (!_tagVersionWatcher.HasChanged(CurrentInventoryVersion()))
        {
            return;
        }

        var tagCounts = InventoryTagQueries.GetTagCounts(componentManager, itemCatalog, _entityId);
        _tabbedContent.SetTabs(BuildTabDefinitions(tagCounts));
    }

    private uint CurrentInventoryVersion() => componentManager.GetMultiPool<InventoryItemStackComponent>().GetEntityVersion(_entityId);

    private List<TabbedContent.TabDefinition> BuildTabDefinitions(List<(Tag Tag, int Count)> tagCounts)
    {
        var definitions = new List<TabbedContent.TabDefinition>(tagCounts.Count + 1)
        {
            new("All", CreateTabContent(null)),
        };

        foreach (var (tag, _) in tagCounts)
        {
            definitions.Add(new TabbedContent.TabDefinition(tag.ToString(), CreateTabContent(tag)));
        }

        return definitions;
    }

    private InventoryTabContent CreateTabContent(Tag? filterTag)
    {
        var gridContent = new InventoryGridContent(componentManager, itemCatalog, elementPoolService, fontService, glyphRenderer, spriteSheetService, spriteRenderer, _entityId, filterTag, _hoverPopup);
        return new InventoryTabContent(elementPoolService, fontService, glyphRenderer, gridContent);
    }
}
