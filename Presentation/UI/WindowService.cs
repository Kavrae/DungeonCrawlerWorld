using Presentation.Fonts;

namespace Presentation.UI;

/// <summary>
/// Pools and constructs windows by type. CreateWindow invokes the registered factory for a
/// type when the pool is empty, which is what lets Window take constructor-injected
/// dependencies instead of pulling them from a locator, since factories close over
/// FontService/WindowService once here.
/// </summary>
public sealed class WindowService
{
    private readonly Dictionary<Type, Stack<Window>> _windowPoolsByType = [];
    private readonly Dictionary<Type, Func<Window?, WindowOptions, Window>> _windowFactoriesByType = [];

    private const int DefaultPoolGrowthSize = 8;
    private const int WindowPoolMaximumSize = byte.MaxValue;

    public WindowService(FontService fontService)
    {
        ArgumentNullException.ThrowIfNull(fontService);

        RegisterFactory<Window>((_, _) => new Window(fontService, this));
        RegisterFactory<TextWindow>((_, _) => new TextWindow(fontService, this));
    }

    /// <summary>
    /// Not generic over an options type: WindowOptions composes independent option groups
    /// (see WindowOptions/WindowLayoutOptions/etc.) instead of being subclassed per window
    /// type, so every window type's factory takes the same WindowOptions.
    /// </summary>
    public void RegisterFactory<TWindow>(Func<Window?, WindowOptions, TWindow> factory)
        where TWindow : Window
    {
        ArgumentNullException.ThrowIfNull(factory);

        _windowFactoriesByType[typeof(TWindow)] = factory;
        _windowPoolsByType.TryAdd(typeof(TWindow), new Stack<Window>(DefaultPoolGrowthSize));
    }

    public TWindow CreateWindow<TWindow>(Window? parentWindow, WindowOptions windowOptions)
        where TWindow : Window
    {
        TWindow window;
        if (_windowPoolsByType.TryGetValue(typeof(TWindow), out var pool) && pool.Count > 0)
        {
            window = (TWindow)pool.Pop();
        }
        else
        {
            if (!_windowFactoriesByType.TryGetValue(typeof(TWindow), out var factory))
            {
                throw new InvalidOperationException($"No factory registered for window type {typeof(TWindow).Name}. Call RegisterFactory first.");
            }

            window = (TWindow)factory(parentWindow, windowOptions);
        }

        window.BuildWindow(parentWindow, windowOptions);
        return window;
    }

    public void CloseWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_windowPoolsByType.TryGetValue(window.GetType(), out var pool) && pool.Count < WindowPoolMaximumSize)
        {
            window.IsVisible = false;
            pool.Push(window);
        }

        window.ParentWindow?.RemoveChildWindow(window.WindowId);
    }
}
