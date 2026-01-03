using DungeonCrawlerWorld.GameManagers.UserInterfaceManager;
using System;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.Services
{
    public interface IWindowService
    {
        public T CreateWindow<T, TOptions>(Window parentWindow, TOptions options)
            where T : Window, new()
            where TOptions : WindowOptions;

        public void CloseWindow(Window window);

        public void RegisterFactory<T, TOptions>(Func<Window, TOptions, T> factory)
            where T : Window
            where TOptions : WindowOptions;
    }

    public class WindowService : IWindowService
    {
        private readonly Dictionary<Type, Stack<Window>> windowPools = [];
        private readonly Dictionary<Type, Delegate> windowPoolFactories = [];

        private readonly byte defaultPoolGrowthSize = 8;
        private readonly byte windowPoolMaximumSize = byte.MaxValue;

        public WindowService()
        {
            RegisterFactory<Window, WindowOptions>((parent, opts) => new Window());
            RegisterFactory<TextWindow, TextWindowOptions>((parent, opts) => new TextWindow());
        }

        public T CreateWindow<T, TOptions>(Window parentWindow, TOptions windowOptions)
            where T : Window, new()
            where TOptions : WindowOptions
        {
            T window;
            if (windowPools.TryGetValue(typeof(T), out var stack) && stack.Count > 0)
            {
                window = (T)stack.Pop();
            }
            else
            {
                window = new T();
            }
            window.BuildWindow(parentWindow, windowOptions);
            return window;
        }

        public void RegisterFactory<T, TOptions>(Func<Window, TOptions, T> factory)
            where T : Window
            where TOptions : WindowOptions
        {
            var type = typeof(T);
            windowPoolFactories[type] = factory;

            if (!windowPools.ContainsKey(type))
            {
                windowPools[type] = new Stack<Window>(defaultPoolGrowthSize);
            }
        }

        public void CloseWindow(Window window)
        {
            if (window == null)
            {
                return;
            }

            if (windowPools.TryGetValue(window.GetType(), out var stack) && stack.Count < windowPoolMaximumSize)
            {
                window.IsVisible = false;
                stack.Push(window);
            }

            window.ParentWindow?.RemoveChildWindow(window.WindowId);
        }
    }
}