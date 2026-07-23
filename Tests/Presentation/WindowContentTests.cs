using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
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
        public List<Keys> PressedKeys { get; } = [];
        public int HandleHotkeysCount { get; private set; }
        public List<char> TypedCharacters { get; } = [];

        public void Initialize(Window hostWindow) => InitializedWith = hostWindow;
        public void Update(GameTime gameTime) => UpdateCount++;
        public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle) => DrawContentCount++;
        public void HandleKeyPress(Keys key) => PressedKeys.Add(key);
        public void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState) => HandleHotkeysCount++;
        public void HandleTextInput(char character) => TypedCharacters.Add(character);
    }

    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

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
    public void HandleKeyPress_ContentAttached_IsForwardedByDefaultVirtualMethod()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        var content = new RecordingContent();
        window.SetContent(content);
        window.Initialize();

        window.HandleKeyPress(Keys.A);

        CollectionAssert.AreEqual(new[] { Keys.A }, content.PressedKeys);
    }

    [TestMethod]
    public void Window_WithNoContentAttached_HandleKeyPressDoesNotThrow()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        window.Initialize();

        window.HandleKeyPress(Keys.A);
    }

    [TestMethod]
    public void HandleHotkeys_ContentAttached_IsForwardedByDefaultVirtualMethod()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        var content = new RecordingContent();
        window.SetContent(content);
        window.Initialize();

        window.HandleHotkeys(new KeyboardState(), new KeyboardState());

        Assert.AreEqual(1, content.HandleHotkeysCount);
    }

    [TestMethod]
    public void Window_WithNoContentAttached_HandleHotkeysDoesNotThrow()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        window.Initialize();

        window.HandleHotkeys(new KeyboardState(), new KeyboardState());
    }

    [TestMethod]
    public void HandleTextInput_ContentAttached_IsForwardedByDefaultVirtualMethod()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        var content = new RecordingContent();
        window.SetContent(content);
        window.Initialize();

        window.HandleTextInput('a');

        CollectionAssert.AreEqual(new[] { 'a' }, content.TypedCharacters);
    }

    [TestMethod]
    public void Window_WithNoContentAttached_HandleTextInputDoesNotThrow()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<Window>(null, new WindowOptions());
        window.Initialize();

        window.HandleTextInput('a');
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