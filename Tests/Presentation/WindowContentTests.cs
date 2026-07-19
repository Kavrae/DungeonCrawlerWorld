using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Validates the IWindowContent hook wiring itself (Initialize/Update/DrawContent all
/// forward from Window to whatever SetContent attached), independent of any concrete
/// content implementation -- this is the actual "pluggable content abstraction" the plan
/// asks Phase 4 to validate before DebugWindow/SelectionWindow/NotificationCenter commit to it.
/// </summary>
[TestClass]
public sealed class WindowContentTests
{
    private sealed class RecordingContent : IWindowContent
    {
        public Window? InitializedWith { get; private set; }
        public int UpdateCount { get; private set; }
        public int DrawContentCount { get; private set; }

        public void Initialize(Window hostWindow) => InitializedWith = hostWindow;
        public void Update(GameTime gameTime) => UpdateCount++;
        public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle) => DrawContentCount++;
    }

    private static WindowService CreateWindowService() => new(new FontService("Fonts"));

    [TestMethod]
    public void Initialize_ContentAttached_ReceivesHostWindow()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        var content = new RecordingContent();
        window.SetContent(content);

        window.Initialize();

        Assert.AreSame(window, content.InitializedWith);
    }

    [TestMethod]
    public void Update_ContentAttached_IsCalledEveryUpdate()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        var content = new RecordingContent();
        window.SetContent(content);
        window.Initialize();

        window.Update(new GameTime());
        window.Update(new GameTime());

        Assert.AreEqual(2, content.UpdateCount);
    }

    [TestMethod]
    public void DrawContent_ContentAttached_IsForwardedByDefaultVirtualMethod()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        var content = new RecordingContent();
        window.SetContent(content);
        window.Initialize();

        window.DrawContent(new GameTime(), null!, null!);

        Assert.AreEqual(1, content.DrawContentCount);
    }

    [TestMethod]
    public void Window_WithNoContentAttached_DrawContentDoesNotThrow()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        window.Initialize();

        window.DrawContent(new GameTime(), null!, null!);
    }
}
