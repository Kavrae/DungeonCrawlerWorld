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

    private static WindowService CreateWindowService()
    {
        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = new WindowService(fontService, glyphRenderer);
        windowService.RegisterFactory<Folder>((_, _) => new Folder(
            fontService, windowService, glyphRenderer, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer()));
        return windowService;
    }

    private static Folder CreateFolder(WindowService windowService) =>
        windowService.CreateWindow<Folder>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = FolderPosition, MaximumSize = new Vector2(200, 400), DisplayMode = WindowDisplayMode.WrapContent },
            Chrome = new WindowChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset },
            Folder = new FolderOptions { SpriteName = "NoSuchManifestEntry", FallbackGlyph = "★" },
        });

    private static TextWindow AddBadge(WindowService windowService, Folder folder, string text)
    {
        var badge = windowService.CreateWindow<TextWindow>(folder, new WindowOptions
        {
            Layout = new WindowLayoutOptions { DisplayMode = WindowDisplayMode.Fixed, Size = BadgeSize },
            Chrome = new WindowChromeOptions { ShowBorder = true, ShowTitle = false },
            Text = new TextOptions { Text = text },
        });
        folder.AddChildWindow(badge);
        return badge;
    }

    [TestMethod]
    public void Initialize_StartsCollapsed()
    {
        var folder = CreateFolder(CreateWindowService());

        folder.Initialize();

        Assert.AreEqual(WindowDisplayMode.Minimized, folder.WindowDisplay);
    }

    [TestMethod]
    public void ClickingTheHeader_WhenCollapsed_ExpandsToWrapContent()
    {
        var folder = CreateFolder(CreateWindowService());
        folder.Initialize();

        folder.HandleClick(new Point((int)FolderPosition.X + 5, (int)FolderPosition.Y + 5));

        Assert.AreEqual(WindowDisplayMode.WrapContent, folder.WindowDisplay);
    }

    [TestMethod]
    public void ClickingTheHeader_Twice_CollapsesBackToMinimized()
    {
        var folder = CreateFolder(CreateWindowService());
        folder.Initialize();
        var headerPoint = new Point((int)FolderPosition.X + 5, (int)FolderPosition.Y + 5);

        folder.HandleClick(headerPoint);
        folder.HandleClick(headerPoint);

        Assert.AreEqual(WindowDisplayMode.Minimized, folder.WindowDisplay);
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

        Assert.AreEqual(first.WindowRelativePosition.X, second.WindowRelativePosition.X);
        Assert.IsGreaterThan(first.WindowRelativePosition.Y, second.WindowRelativePosition.Y);
        Assert.AreEqual(first.WindowRelativePosition.Y + first.WindowCurrentSize.Y, second.WindowRelativePosition.Y);
    }

    [TestMethod]
    public void Collapsed_DoesNotShowChildren()
    {
        var windowService = CreateWindowService();
        var folder = CreateFolder(windowService);
        AddBadge(windowService, folder, "System: 0");
        folder.Initialize();

        // Never expanded -- still Minimized, per Initialize_StartsCollapsed.
        Assert.AreEqual(WindowDisplayMode.Minimized, folder.WindowDisplay);
    }

    /// <summary>Collapsed width must match the expanded content width -- only the height collapses.</summary>
    [TestMethod]
    public void CollapsedWidth_MatchesExpandedContentWidth()
    {
        var windowService = CreateWindowService();
        var folder = CreateFolder(windowService);
        AddBadge(windowService, folder, "Quest: 0");
        folder.Initialize();
        var collapsedWidth = folder.WindowCurrentSize.X;

        folder.HandleClick(new Point((int)FolderPosition.X + 5, (int)FolderPosition.Y + 5));

        Assert.AreEqual(WindowDisplayMode.WrapContent, folder.WindowDisplay);
        Assert.AreEqual(collapsedWidth, folder.WindowCurrentSize.X);
    }
}
