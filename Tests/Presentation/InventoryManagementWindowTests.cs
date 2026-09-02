using Engine.ECS.Components;
using Engine.Math;
using Game.Modules;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.World;
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
[DoNotParallelize]
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

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<GridControl>(() => new GridControl(fontService, windowService, labelRenderer));
        windowService.RegisterFactory<Toggle>(() => new Toggle(fontService, windowService, labelRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1)));
        var contextMenuController = new ContextMenuController(windowService);
        contextMenuController.Initialize(new UiLayerStack());
        var mapViewState = new MapViewState();

        windowService.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(
            fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer, componentManager, itemCatalog, world, contextMenuController, mapViewState));

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
        window.Configure(EntityId, hoverPopup, static () => null, static (_, _) => { }, static (_, _) => { });
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

    /// <summary>Depth-first search for the Window hosting the active tab's InventoryGridContent (the same Window FindActiveGrid finds via its Tag, but returning the Window itself so a test can walk its child cells in on-screen order).</summary>
    private static Window? FindActiveGridWindow(Element element)
    {
        if (element is Window { Tag: InventoryGridContent } window)
        {
            return window;
        }

        foreach (var child in element.ChildElements)
        {
            if (FindActiveGridWindow(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The active grid's own cells in rendered order -- RebuildCells adds them to its host Window as children in exactly its sorted order, so child order here is sort order.</summary>
    private static List<InventoryItemStackCell> GetOrderedCells(Element root) =>
        FindActiveGridWindow(root)!.ChildElements.OfType<InventoryItemStackCell>().ToList();

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

    [TestMethod]
    public void SortOrder_RecentlyAcquiredDescending_OrdersCellsNewestFirst()
    {
        // Build() already grants firstItemId; secondItemId and scrollItemId are granted here,
        // each after a short sleep, so the three stacks have distinctly ordered FirstAcquiredUtcTicks.
        var (window, componentManager, firstItemId, secondItemId, scrollItemId) = Build();
        Thread.Sleep(5);
        InventoryActions.AddItem(componentManager, EntityId, secondItemId, quantity: 1);
        Thread.Sleep(5);
        InventoryActions.AddItem(componentManager, EntityId, scrollItemId, quantity: 1);

        var grid = FindActiveGrid(window);
        Assert.IsNotNull(grid, "The default-active 'All' tab should already have a grid.");
        grid!.SortOrder = InventorySortOrder.RecentlyAcquiredDescending;

        var orderedItemIds = GetOrderedCells(window).Select(cell => cell.ItemDefinitionId).ToList();
        CollectionAssert.AreEqual(new[] { scrollItemId, secondItemId, firstItemId }, orderedItemIds);
    }

    [TestMethod]
    public void SortOrder_RecentlyAcquiredDescending_MergedStackSortsByItsNewestMember()
    {
        // secondItemId is granted twice as a divergent item (two distinct member stacks merged
        // into one badged cell) -- the second grant, well after firstItemId, must be what the
        // merged cell sorts by, not either member's Quantity or the group's oldest timestamp.
        var (window, componentManager, firstItemId, secondItemId, _) = Build();
        Thread.Sleep(5);
        var secondItemDefinition = new ItemDefinition(secondItemId, "Potion B", null, "b", Color.White, Tags: [Tag.Potion], Effects: []);
        InventoryActions.AddDivergentItem(componentManager, EntityId, secondItemDefinition with { Description = "batch 1" });
        Thread.Sleep(5);
        InventoryActions.AddDivergentItem(componentManager, EntityId, secondItemDefinition with { Description = "batch 2" });

        var grid = FindActiveGrid(window);
        Assert.IsNotNull(grid);
        grid!.SortOrder = InventorySortOrder.RecentlyAcquiredDescending;

        var orderedCells = GetOrderedCells(window);
        var mergedCell = orderedCells.Single(cell => cell.ItemDefinitionId == secondItemId);
        Assert.IsTrue(mergedCell.MergedStackBadgeVisible, "Two divergent stacks of the same item should merge into one badged cell.");
        Assert.AreEqual(0, orderedCells.IndexOf(mergedCell), "The merged cell (newest member acquired after firstItemId) must sort ahead of firstItemId.");
    }
}
