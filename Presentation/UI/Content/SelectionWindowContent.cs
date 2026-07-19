using Engine.Diagnostics;
using Engine.ECS.Components;
using Game.Modules.Core.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI.Content;

/// <summary>
/// Shows every component on the entities at the currently selected map node, one child
/// TextWindow per component -- a diff-and-refresh design that only recreates child windows
/// when the selected entity set changes, and refreshes existing windows' text on an interval
/// rather than every frame. Component text comes from Engine/Diagnostics's
/// ComponentInspector, not a live reflection walk: every component already carries a
/// purpose-built ToString(), so no reflection is needed at all.
/// </summary>
public sealed class SelectionWindowContent(
    World world,
    ComponentManager componentManager,
    ComponentInspector componentInspector,
    WindowService windowService) : IWindowContent
{
    private const int ComponentRefreshInterval = 10; // Most components update every 10 frames, so more frequent refreshes are wasted work.

    private readonly Dictionary<int, List<TextWindow>> _entityDebugWindows = [];
    private readonly HashSet<int> _visibleDebugEntityIds = [];
    private readonly HashSet<int> _selectedEntityIds = [];
    private readonly List<InspectedComponentEntry> _reusableInspectionList = [];

    private Window _hostWindow = null!;
    private int _updatesSinceLastComponentRefresh;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
    }

    public void Update(GameTime gameTime)
    {
        RecomputeSelectedEntityIds();

        foreach (var entityId in _visibleDebugEntityIds.Except(_selectedEntityIds).ToArray())
        {
            if (_entityDebugWindows.Remove(entityId, out var windows))
            {
                foreach (var window in windows)
                {
                    window.Close();
                }
            }

            _visibleDebugEntityIds.Remove(entityId);
        }

        foreach (var entityId in _selectedEntityIds.Except(_visibleDebugEntityIds).ToArray())
        {
            _entityDebugWindows[entityId] = CreateDebugWindowsForEntity(entityId);
            _visibleDebugEntityIds.Add(entityId);
        }

        _updatesSinceLastComponentRefresh++;
        if (_updatesSinceLastComponentRefresh >= ComponentRefreshInterval)
        {
            _updatesSinceLastComponentRefresh = 0;
            foreach (var entityId in _visibleDebugEntityIds)
            {
                RefreshDebugWindowsForEntity(entityId);
            }
        }

        _hostWindow.TitleText = world.SelectedMapNodePosition is { } selected
            ? $"Selected Map Node : {selected.X},{selected.Y}"
            : "No map nodes selected";
    }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        // Nothing to draw directly -- everything is child TextWindows, which Window already
        // draws as part of its own child-window loop.
    }

    private void RecomputeSelectedEntityIds()
    {
        _selectedEntityIds.Clear();

        if (world.SelectedMapNodePosition is not { } selected)
        {
            return;
        }

        // SelectedMapNodePosition is a plain settable property; MapWindow is the only
        // current writer and already validates on-map before setting it, but nothing
        // enforces that here, so guard directly against indexing MapNodes out of bounds.
        if (!world.IsOnMap(new Engine.Math.Vector3Int(selected.X, selected.Y, 0)))
        {
            return;
        }

        for (var z = world.Map.Size.Z - 1; z >= 0; z--)
        {
            var entityId = world.Map.MapNodes[selected.X, selected.Y, z].EntityId;
            if (entityId != -1)
            {
                _selectedEntityIds.Add(entityId);
            }
        }
    }

    private List<TextWindow> CreateDebugWindowsForEntity(int entityId)
    {
        var createdWindows = new List<TextWindow>();
        var displayTextPool = componentManager.GetDirectPool<DisplayTextComponent>();

        if (displayTextPool.Has(entityId))
        {
            var nameWindow = windowService.CreateWindow<TextWindow>(_hostWindow, new WindowOptions
            {
                Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = false },
                Layout = new WindowLayoutOptions { MaximumSize = _hostWindow.ContentSize, DisplayMode = WindowDisplayMode.Grow },
                Chrome = new WindowChromeOptions { ShowTitle = false, ShowBorder = false },
                Text = new TextOptions { Text = displayTextPool.GetReadonly(entityId).Name },
            });
            _hostWindow.AddChildWindow(nameWindow);
            createdWindows.Add(nameWindow);
        }

        _reusableInspectionList.Clear();
        componentInspector.CopyInspectionDataForEntity(entityId, _reusableInspectionList);

        foreach (var entry in _reusableInspectionList)
        {
            var componentWindow = windowService.CreateWindow<TextWindow>(_hostWindow, new WindowOptions
            {
                Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = false },
                Layout = new WindowLayoutOptions { MaximumSize = _hostWindow.ContentSize, DisplayMode = WindowDisplayMode.Grow },
                Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = entry.ComponentType.Name, ShowBorder = true, BorderSize = new Vector2(1, 1) },
                Text = new TextOptions { Text = entry.Value.ToString() ?? string.Empty },
            });
            _hostWindow.AddChildWindow(componentWindow);
            createdWindows.Add(componentWindow);
        }

        return createdWindows;
    }

    private void RefreshDebugWindowsForEntity(int entityId)
    {
        if (!_entityDebugWindows.TryGetValue(entityId, out var windows))
        {
            return;
        }

        var windowIndex = 0;
        var displayTextPool = componentManager.GetDirectPool<DisplayTextComponent>();

        if (displayTextPool.Has(entityId) && windowIndex < windows.Count)
        {
            windows[windowIndex].UpdateText(displayTextPool.GetReadonly(entityId).Name);
            windowIndex++;
        }

        _reusableInspectionList.Clear();
        componentInspector.CopyInspectionDataForEntity(entityId, _reusableInspectionList);

        foreach (var entry in _reusableInspectionList)
        {
            if (windowIndex >= windows.Count)
            {
                break;
            }

            windows[windowIndex].UpdateText(entry.Value.ToString() ?? string.Empty);
            windowIndex++;
        }
    }
}
