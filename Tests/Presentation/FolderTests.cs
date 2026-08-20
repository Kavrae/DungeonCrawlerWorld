using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>Covers Folder's expand/collapse toggle and child tiling. Icon rendering itself (sprite vs. glyph fallback) is drawn through GraphicsDevice/SpriteBatch and isn't unit-testable headlessly -- verified in-game instead.</summary>
[TestClass]
public sealed class FolderTests
{
    private static readonly Vector2 FolderPosition = new(30, 30);
    private static readonly Vector2 BadgeSize = new(130, 21);

    private static ElementPoolService CreateWindowService()
    {
        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);
        windowService.RegisterFactory<Folder>(() => new Folder(
            fontService, windowService, glyphRenderer, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer()));
        return windowService;
    }

    private static Folder CreateFolder(ElementPoolService windowService) =>
        windowService.CreateElement<Folder>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = FolderPosition, MaximumSize = new Vector2(200, 400), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset },
            Folder = new FolderOptions { SpriteName = "NoSuchManifestEntry", FallbackGlyph = "★" },
        });

    private static TextWindow AddBadge(ElementPoolService windowService, Folder folder, string text)
    {
        var badge = windowService.CreateElement<TextWindow>(folder, new ElementOptions
        {
            Layout = new ElementLayoutOptions { DisplayMode = ElementDisplayMode.Fixed, Size = BadgeSize },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
            Text = new TextOptions { Text = text },
        });
        folder.AddChild(badge);
        return badge;
    }

    [TestMethod]
    public void Initialize_StartsCollapsed()
    {
        var folder = CreateFolder(CreateWindowService());

        folder.Initialize();

        Assert.AreEqual(ElementDisplayMode.Minimized, folder.DisplayMode);
    }

    [TestMethod]
    public void ClickingTheHeader_WhenCollapsed_ExpandsToWrapContent()
    {
        var folder = CreateFolder(CreateWindowService());
        folder.Initialize();

        folder.HandleClick(new Point((int)FolderPosition.X + 5, (int)FolderPosition.Y + 5));

        Assert.AreEqual(ElementDisplayMode.WrapContent, folder.DisplayMode);
    }

    [TestMethod]
    public void ClickingTheHeader_Twice_CollapsesBackToMinimized()
    {
        var folder = CreateFolder(CreateWindowService());
        folder.Initialize();
        var headerPoint = new Point((int)FolderPosition.X + 5, (int)FolderPosition.Y + 5);

        folder.HandleClick(headerPoint);
        folder.HandleClick(headerPoint);

        Assert.AreEqual(ElementDisplayMode.Minimized, folder.DisplayMode);
    }

    [TestMethod]
    public void Children_AddedBeforeInitialize_TileVerticallyOnceExpanded()
    {
        var windowService = CreateWindowService();
        var folder = CreateFolder(windowService);
        var first = AddBadge(windowService, folder, "System: 0");
        var second = AddBadge(windowService, folder, "Quest: 0");
        folder.Initialize();

        folder.HandleClick(new Point((int)FolderPosition.X + 5, (int)FolderPosition.Y + 5));

        Assert.AreEqual(first.RelativePosition.X, second.RelativePosition.X);
        Assert.IsGreaterThan(first.RelativePosition.Y, second.RelativePosition.Y);
        Assert.AreEqual(first.RelativePosition.Y + first.CurrentSize.Y, second.RelativePosition.Y);
    }

    [TestMethod]
    public void Collapsed_DoesNotShowChildren()
    {
        var windowService = CreateWindowService();
        var folder = CreateFolder(windowService);
        AddBadge(windowService, folder, "System: 0");
        folder.Initialize();

        // Never expanded -- still Minimized, per Initialize_StartsCollapsed.
        Assert.AreEqual(ElementDisplayMode.Minimized, folder.DisplayMode);
    }

    /// <summary>
    /// Regression guard: collapsing a Folder that's been expanded must shrink it back to its
    /// icon size in both dimensions, not just height. Confirmed bug: RecalculateMinimizedSize
    /// used to derive width from the children's own already-measured CurrentSize, so a Folder
    /// stayed as wide as whatever it last expanded to on every subsequent close -- only its
    /// height ever actually collapsed.
    /// </summary>
    [TestMethod]
    public void Collapsing_AfterHavingBeenExpanded_ShrinksBackToIconSize()
    {
        var windowService = CreateWindowService();
        var folder = CreateFolder(windowService);
        AddBadge(windowService, folder, "Quest: 0");
        folder.Initialize();
        var iconSize = folder.CurrentSize;
        var headerPoint = new Point((int)FolderPosition.X + 5, (int)FolderPosition.Y + 5);

        folder.HandleClick(headerPoint);
        Assert.AreEqual(ElementDisplayMode.WrapContent, folder.DisplayMode);
        Assert.IsGreaterThan(iconSize.X, folder.CurrentSize.X);

        folder.HandleClick(headerPoint);

        Assert.AreEqual(ElementDisplayMode.Minimized, folder.DisplayMode);
        Assert.AreEqual(iconSize, folder.CurrentSize);
    }
}
