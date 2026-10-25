using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;
using Workshop.Debug;

namespace Workshop.Tests;

/// <summary>The Debug tab's view over a real layer stack, element pool and UiInputController -- input goes in as frames of mouse state, and each frame updates every layer and the view, as the shell does.</summary>
/// <remarks>Its one FontService is shared by the whole run and its users are [DoNotParallelize]: FreeType's handles and the glyph atlas are neither safe to have several of nor to share across threads.</remarks>
internal sealed class DebugTabHarness : IDisposable
{
    public static readonly Vector2 ScreenSize = new(1600, 900);

    private static readonly FontService SharedFonts = new("Fonts");

    private MouseState _mouse;
    private KeyboardState _keyboard;

    /// <param name="launchTarget">What Play would start; a game that isn't there by default.</param>
    public DebugTabHarness(string optionsPath, IReadOnlyList<string>? reportDirectories = null, GameLaunchTarget? launchTarget = null)
    {
        var fonts = SharedFonts;
        var render = new UiRenderService();
        Pool = new ElementPoolService();
        Layers = new UiLayerStack();
        Pool.RegisterFactory(() => new Window(fonts, Pool, render));
        Pool.RegisterFactory(() => new TextWindow(fonts, Pool, render));
        Pool.RegisterFactory(() => new Button(fonts, Pool, render));
        Pool.RegisterFactory(() => new Dropdown(fonts, Pool, render));
        Pool.RegisterFactory(() => new Toggle(fonts, Pool, render));
        Pool.RegisterFactory(() => new ToggleList(fonts, Pool, render));
        Pool.RegisterFactory(() => new TextBox(fonts, Pool, render));
        Pool.RegisterFactory(() => new ListView(fonts, Pool, render));
        Pool.RegisterFactory(() => new NumericField(fonts, Pool, render));
        Pool.RegisterFactory(() => new FractionBarElement(fonts, Pool, render));
        var dropdowns = new DropdownController(Pool, render) { ScreenBoundsOverrideForTests = new Rectangle(0, 0, (int)ScreenSize.X, (int)ScreenSize.Y) };
        dropdowns.Initialize(Layers);
        Pool.RegisterFactory(() => new DropdownField(fonts, Pool, render, dropdowns));
        DebugTabServices.RegisterElementFactories(Pool, fonts, render);
        Input = new UiInputController(Layers, ScreenSize, new PointerState(), [], [], dropdowns) { StartTextInput = static () => { }, StopTextInput = static () => { } };

        Launcher = new GameLauncher(launchTarget ?? new GameLaunchTarget(Path.Combine("bin", "Game.exe"), "--map-size=1024", "bin"));
        Debugging = new WorkshopDebugging(new DiagnosticsLinkListener(historyCapacity: 2_000), new DebugLaunchOptionsStore(optionsPath), Launcher, reportDirectories);
        Debugging.Link.Start();
        ContextMenus = new ContextMenuController(dropdowns);
        View = new DebugTabView(new DebugTabServices(Pool, fonts, render, Layers, Input.AddInputHandler, Status, Clipboard.Add, ContextMenus), Debugging);
        View.Open(new Rectangle(0, 30, (int)ScreenSize.X, (int)ScreenSize.Y - 30));
    }

    public ElementPoolService Pool { get; }

    public UiLayerStack Layers { get; }

    public UiInputController Input { get; }

    public GameLauncher Launcher { get; }

    public WorkshopDebugging Debugging { get; }

    public DebugTabView View { get; }

    public ContextMenuController ContextMenus { get; }

    /// <summary>Everything copied to the clipboard, oldest first.</summary>
    public List<string> Clipboard { get; } = [];

    public RecordingStatus Status { get; } = new();

    /// <summary>What the Live section shows instead of the listener's session, when set.</summary>
    public LiveDiagnosticsSession? Session { get; set; }

    /// <summary>One frame: the input, every layer's update, the link and the run's status, then the view's.</summary>
    public void Frame()
    {
        Input.Update(_keyboard, _mouse);
        Debugging.Link.Update();
        Debugging.Update(Status);
        foreach (var layer in UiLayerStack.LayersAscending())
        {
            foreach (var element in Layers[layer].ToArray())
            {
                element.Update(new GameTime());
            }
        }

        if (Session is not null && View.Section == DebugSection.Live)
        {
            View.Live.Update(Session);
        }
        else
        {
            View.Update();
        }
    }

    public void MoveTo(Point position)
    {
        _mouse = new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
    }

    public void Click(Point position)
    {
        MoveTo(position);
        _mouse = new MouseState(position.X, position.Y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
        _mouse = new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
    }

    public void Click(Element element) => Click(element.Rectangle.Center);

    /// <summary>Presses at from, moves to to while held, and releases there.</summary>
    public void Drag(Point from, Point to)
    {
        MoveTo(from);
        _mouse = new MouseState(from.X, from.Y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
        _mouse = new MouseState(to.X, to.Y, 0, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
        _mouse = new MouseState(to.X, to.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
    }

    public void RightClick(Point position)
    {
        MoveTo(position);
        _mouse = new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Pressed, ButtonState.Released, ButtonState.Released);
        Frame();
        _mouse = new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
    }

    /// <summary>Presses key for one frame and releases it the next.</summary>
    public void Press(Keys key)
    {
        _keyboard = new KeyboardState(key);
        Frame();
        _keyboard = new KeyboardState();
        Frame();
    }

    /// <summary>Types text into whatever has focus.</summary>
    public void Type(string text)
    {
        foreach (var character in text)
        {
            Input.OnTextInput(character);
        }

        Frame();
    }

    public void Dispose()
    {
        View.Close();
        Debugging.Dispose();
        Launcher.Dispose();
    }

    /// <summary>The status line: what it was last told to show.</summary>
    public sealed class RecordingStatus : IWorkshopStatus
    {
        public string LastMessage { get; private set; } = string.Empty;

        public void Show(string message) => LastMessage = message;
    }
}
