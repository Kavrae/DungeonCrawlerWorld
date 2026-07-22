using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Confirms the decomposed option groups (WindowHierarchyOptions/WindowLayoutOptions/
/// WindowChromeOptions/WindowContentOptions/TextOptions) apply correctly and independently,
/// and that a window with no options set at all still gets sane defaults -- the two
/// properties the old single-inheritance WindowOptions/TextWindowOptions pair couldn't
/// offer at once (a TextWindow needing a non-text option group had no way to combine them
/// without a new subclass).
/// </summary>
[TestClass]
public sealed class WindowOptionsTests
{
    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    [TestMethod]
    public void CreateWindow_AllGroupsUnset_FallsBackToDefaults()
    {
        var windowService = CreateWindowService();

        var window = windowService.CreateWindow<Window>(null, new WindowOptions());

        Assert.AreEqual(WindowDisplayMode.Fixed, window.WindowDisplay);
        Assert.IsFalse(window.ShowTitle);
        Assert.IsFalse(window.ShowBorder);
        Assert.IsFalse(window.CanContainChildWindows);
        Assert.IsTrue(window.IsVisible);
        Assert.AreEqual(string.Empty, window.TitleText);
        // Opt-out, not opt-in, unlike every other CanUserXxx flag above -- see Window.CanUserFocus.
        Assert.IsTrue(window.CanUserFocus);
    }

    /// <summary>The one concrete opt-out case today: the debug stats window (see GameShellBootstrapper) has nothing that needs keyboard input and shouldn't be a stop in the Tab sequence.</summary>
    [TestMethod]
    public void CreateWindow_ChromeGroup_CanUserFocusFalse_OptsOutOfFocus()
    {
        var windowService = CreateWindowService();

        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Chrome = new WindowChromeOptions { CanUserFocus = false },
        });

        Assert.IsFalse(window.CanUserFocus);
    }

    [TestMethod]
    public void CreateWindow_LayoutGroup_AppliesIndependentlyOfChrome()
    {
        var windowService = CreateWindowService();

        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions
            {
                RelativePosition = new Vector2(10, 20),
                Size = new Vector2(100, 50),
                DisplayMode = WindowDisplayMode.Fixed,
            },
        });

        Assert.AreEqual(new Vector2(10, 20), window.WindowRelativePosition);
        Assert.AreEqual(new Vector2(100, 50), window.WindowOriginalSize);
        // Chrome was never set -- layout applying correctly shouldn't depend on it.
        Assert.IsFalse(window.ShowTitle);
        Assert.IsFalse(window.ShowBorder);
    }

    [TestMethod]
    public void CreateWindow_ChromeGroup_AppliesIndependentlyOfLayout()
    {
        var windowService = CreateWindowService();

        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Chrome = new WindowChromeOptions
            {
                ShowTitle = true,
                TitleText = "Hello",
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = true,
            },
        });

        Assert.IsTrue(window.ShowTitle);
        Assert.AreEqual("Hello", window.TitleText);
        Assert.IsTrue(window.ShowBorder);
        Assert.IsTrue(window.CanUserClose);
        Assert.IsTrue(window.CanUserMinimize);
        // Layout was never set -- chrome applying correctly shouldn't depend on it.
        Assert.AreEqual(WindowDisplayMode.Fixed, window.WindowDisplay);
    }

    [TestMethod]
    public void CreateWindow_HierarchyGroup_Applies()
    {
        var windowService = CreateWindowService();

        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
        });

        Assert.IsTrue(window.CanContainChildWindows);
        Assert.AreEqual(WindowTileMode.Vertical, window.ChildWindowTileMode);
    }

    [TestMethod]
    public void CreateWindow_ContentGroup_Applies()
    {
        var windowService = CreateWindowService();

        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Content = new WindowContentOptions { ContentColor = Color.Red },
        });

        Assert.AreEqual(Color.Red, window.ContentColor);
    }

    /// <summary>
    /// TextWindow reads WindowOptions.Text directly -- no TextWindowOptions subclass is
    /// needed, and a TextWindow can combine Text with any other group (e.g. Chrome) the same
    /// way any other window type would.
    /// </summary>
    [TestMethod]
    public void CreateWindow_TextWindowWithTextAndChromeGroups_AppliesBoth()
    {
        var windowService = CreateWindowService();

        var window = windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Notes" },
            Text = new TextOptions { Text = "Hello world", TextColor = Color.Blue },
        });

        Assert.AreEqual("Hello world", window.OriginalText);
        Assert.AreEqual(Color.Blue, window.TextColor);
        Assert.IsTrue(window.ShowTitle);
        Assert.AreEqual("Notes", window.TitleText);
    }

    [TestMethod]
    public void CreateWindow_TextWindowWithNoTextGroup_DefaultsToEmptyText()
    {
        var windowService = CreateWindowService();

        var window = windowService.CreateWindow<TextWindow>(null, new WindowOptions());

        Assert.AreEqual(string.Empty, window.OriginalText);
    }
}
