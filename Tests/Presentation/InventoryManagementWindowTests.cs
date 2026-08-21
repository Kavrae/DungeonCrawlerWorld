using Engine.ECS.Components;
using Engine.Math;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Inventory;

namespace Tests.Presentation;

/// <summary>
/// Covers InventoryManagementWindow.Update's tab-rebuild gating -- see its own doc comment for
/// the bug this protects against: rebuilding the tab list on every inventory version bump
/// (confirmed by live testing) reset the active tab's own sort/toggle/search state, and could
/// reset tab selection outright, on every single item add/remove, not just when a tag was
/// actually gained or lost.
/// </summary>
[TestClass]
public sealed class InventoryManagementWindowTests
{
    private const int EntityId = 1;

    private static (InventoryManagementWindow Window, ComponentManager ComponentManager, Guid FirstItemId, Guid SecondItemId, Guid ScrollItemId) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);

        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var scrollItemId = Guid.NewGuid();
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(firstItemId, "Potion A", null, "a", Color.White, Tags: [Tag.Potion], Effects: []));
        itemCatalog.Register(new ItemDefinition(secondItemId, "Potion B", null, "b", Color.White, Tags: [Tag.Potion], Effects: []));
        itemCatalog.Register(new ItemDefinition(scrollItemId, "Scroll", null, "s", Color.White, Tags: [Tag.Scroll], Effects: []));
        InventoryActions.AddItem(componentManager, EntityId, firstItemId, quantity: 1);

        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, glyphRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<GridControl>(() => new GridControl(fontService, windowService, glyphRenderer));
        windowService.RegisterFactory<Toggle>(() => new Toggle(fontService, windowService, glyphRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, glyphRenderer));
        windowService.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(
            fontService, windowService, glyphRenderer, spriteSheetService, spriteRenderer, componentManager, itemCatalog));

        var hoverPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        hoverPopup.Initialize();

        var window = windowService.CreateElement<InventoryManagementWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(300, 300), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false },
        });
        window.Configure(EntityId, hoverPopup);
        window.Initialize();

        return (window, componentManager, firstItemId, secondItemId, scrollItemId);
    }

    /// <summary>Depth-first search for the first InventoryGridContent any descendant Window's Tag currently references -- see InventoryGridContent.Initialize's own doc comment for why Tag, not Content, is the reliable way to find it.</summary>
    private static InventoryGridContent? FindActiveGrid(Element element)
    {
        if (element is Window { Tag: InventoryGridContent grid })
        {
            return grid;
        }

        foreach (var child in element.ChildElements)
        {
            if (FindActiveGrid(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Depth-first search for a tab header tile (TextWindow) with the given label.</summary>
    private static TextWindow? FindTabTile(Element element, string label)
    {
        if (element is TextWindow { OriginalText: var text } tile && text == label)
        {
            return tile;
        }

        foreach (var child in element.ChildElements)
        {
            if (FindTabTile(child, label) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    [TestMethod]
    public void Update_QuantityChangeWithinSameTagSet_DoesNotRebuildTheActiveTabsGrid()
    {
        var (window, componentManager, _, secondItemId, _) = Build();

        var tabTile = FindTabTile(window, Tag.Potion.ToString());
        Assert.IsNotNull(tabTile, "Expected a Potion tab to exist for the item granted in Build.");
        tabTile!.HandleClick(tabTile.ContentRectangle.Center);

        var gridBeforeUpdate = FindActiveGrid(window);
        Assert.IsNotNull(gridBeforeUpdate);
        gridBeforeUpdate!.HideDisabled = true;

        // A second stack sharing the same tag -- the tag SET stays {Potion}, only a stack count/
        // version bump happens, the exact case that must not trigger a tab rebuild.
        InventoryActions.AddItem(componentManager, EntityId, secondItemId, quantity: 1);
        window.Update(new GameTime());

        var gridAfterUpdate = FindActiveGrid(window);
        Assert.IsNotNull(gridAfterUpdate);
        Assert.AreSame(gridBeforeUpdate, gridAfterUpdate, "The active tab's own InventoryGridContent instance must survive a same-tag-set inventory update, not be rebuilt from scratch.");
        Assert.IsTrue(gridAfterUpdate!.HideDisabled, "HideDisabled (and by extension every other per-tab toggle/sort/search state) must survive a same-tag-set inventory update -- and since a rebuild would replace every tab's grid, not just the active one, this also proves tab selection itself was never disturbed.");
    }

    [TestMethod]
    public void Update_NewTagAppears_RebuildsAndAddsTheNewTab()
    {
        var (window, componentManager, _, _, scrollItemId) = Build();

        // A distinct, previously-unrepresented tag -- the genuine "tab list changed" case, which
        // must still rebuild (unlike the same-tag-set case above) so the new tab actually appears.
        InventoryActions.AddItem(componentManager, EntityId, scrollItemId, quantity: 1);
        window.Update(new GameTime());

        Assert.IsNotNull(FindTabTile(window, Tag.Potion.ToString()), "The pre-existing Potion tab should still exist.");
        Assert.IsNotNull(FindTabTile(window, Tag.Scroll.ToString()), "A newly-represented tag should get its own tab once the tab list actually changes.");
    }
}
