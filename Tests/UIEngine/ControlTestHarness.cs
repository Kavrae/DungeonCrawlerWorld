using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>A layer stack, the shared dropdown, every editor control's factory and a UiInputController over them -- input goes in as frames of keyboard and mouse state, and each frame updates every layer as the host does.</summary>
internal sealed class ControlTestHarness
{
    public static readonly Vector2 ScreenSize = new(1920, 1080);

    private KeyboardState _keyboard;
    private MouseState _mouse;

    public ControlTestHarness()
    {
        var fontService = TestFonts.Shared;
        Pool = TestElementPoolServiceFactory.Create(fontService);
        Layers = new UiLayerStack();
        Dropdowns = TestElementPoolServiceFactory.CreateDropdownController(Pool, Layers);
        var contextMenus = new ContextMenuController(Dropdowns);
        Pool.RegisterFactory(() => new NumericField(fontService, Pool, new UiRenderService(), null, contextMenus));
        Pool.RegisterFactory(() => new ColorField(fontService, Pool, new UiRenderService(), null, contextMenus));
        Pool.RegisterFactory(() => new DropdownField(fontService, Pool, new UiRenderService(), Dropdowns));
        Pool.RegisterFactory(() => new ListView(fontService, Pool, new UiRenderService()));
        Pool.RegisterFactory(() => new ListEditor(fontService, Pool, new UiRenderService()));
        Pool.RegisterFactory(() => new SplitPane(fontService, Pool, new UiRenderService()));
        Pool.RegisterFactory(() => new ModalDialog(fontService, Pool, new UiRenderService()));
        Input = new UiInputController(Layers, ScreenSize, new PointerState(), [], [], Dropdowns) { StartTextInput = static () => { }, StopTextInput = static () => { } };
    }

    public ElementPoolService Pool { get; }

    public UiLayerStack Layers { get; }

    public DropdownController Dropdowns { get; }

    public UiInputController Input { get; }

    public static Rectangle ScreenBounds => new(0, 0, (int)ScreenSize.X, (int)ScreenSize.Y);

    /// <summary>Creates a root control of type T at position, sized size, configured before Initialize, in DynamicHud.</summary>
    public T AddRoot<T>(Vector2 position, Vector2 size, Action<T>? configure = null, ElementChromeOptions? chrome = null)
        where T : Element
    {
        var element = Pool.CreateElement<T>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MaximumSize = ScreenSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = chrome ?? new ElementChromeOptions { ShowBorder = true },
        });
        configure?.Invoke(element);
        element.Initialize();
        Layers.Add(UiLayer.DynamicHud, element);
        return element;
    }

    /// <summary>One frame: the input, then every layer's update.</summary>
    public void Frame()
    {
        Input.Update(_keyboard, _mouse);
        foreach (var layer in UiLayerStack.LayersAscending())
        {
            foreach (var element in Layers[layer].ToArray())
            {
                element.Update(new GameTime());
            }
        }
    }

    public void MoveTo(Point position)
    {
        _mouse = new MouseState(position.X, position.Y, _mouse.ScrollWheelValue, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
    }

    public void Click(Point position)
    {
        MoveTo(position);
        _mouse = new MouseState(position.X, position.Y, _mouse.ScrollWheelValue, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
        _mouse = new MouseState(position.X, position.Y, _mouse.ScrollWheelValue, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
    }

    public void Click(Element element) => Click(element.Rectangle.Center);

    /// <summary>Presses at from, moves to to, and releases there.</summary>
    public void Drag(Point from, Point to)
    {
        MoveTo(from);
        _mouse = new MouseState(from.X, from.Y, _mouse.ScrollWheelValue, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
        _mouse = new MouseState(to.X, to.Y, _mouse.ScrollWheelValue, ButtonState.Pressed, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
        _mouse = new MouseState(to.X, to.Y, _mouse.ScrollWheelValue, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
    }

    /// <summary>Turns the wheel at position by notches (positive away from the user).</summary>
    public void Wheel(Point position, int notches)
    {
        MoveTo(position);
        _mouse = new MouseState(position.X, position.Y, _mouse.ScrollWheelValue + notches * 120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Frame();
    }

    public void Press(Keys key)
    {
        _keyboard = new KeyboardState(key);
        Frame();
        _keyboard = new KeyboardState();
        Frame();
    }

    public void Type(string text)
    {
        foreach (var character in text)
        {
            Input.OnTextInput(character);
        }

        Frame();
    }
}
