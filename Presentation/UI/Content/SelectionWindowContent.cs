using Engine.Diagnostics;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Core.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI.Content;

/// <summary>
/// Shows every component on the Blocking/Tiny/Phasing entities and the terrain at the
/// currently selected map node's MapViewState.CurrentMapLayer -- the same single layer MapWindow is
/// rendering, not every layer -- one child TextWindow per component. A diff-and-refresh
/// design that only recreates child windows when the selected entity set changes, and
/// refreshes existing windows' text on an interval rather than every frame. Component text
/// comes from Engine/Diagnostics's ComponentInspector, not a live reflection walk: every
/// component already carries a purpose-built ToString(), so no reflection is needed at all.
/// </summary>
public sealed class SelectionWindowContent(
    World world,
    MapViewState mapViewState,
    ComponentManager componentManager,
    ComponentInspector componentInspector,
    WindowService windowService) : IWindowContent
{
    private const int ComponentRefreshInterval = 10; // Most components update every 10 frames, so more frequent refreshes are wasted work.

    /// <summary>A generous, effectively-unlimited per-component-window height cap -- see CreateDebugWindowsForEntity.</summary>
    private const float UnboundedChildHeight = 10000f;

    // Resolved once and reused rather than re-resolved via ComponentManager's dictionary
    // lookup on every call -- RecomputeSelectedEntityIds runs every frame (see Update), so
    // occupancyPool/transformPool were otherwise being looked up 60 times a second for no
    // reason. Matches the pattern MapWindow already uses for its own pool references.
    private readonly PackedComponentPool<OccupancyComponent> _occupancyPool = componentManager.GetPackedPool<OccupancyComponent>();
    private readonly DirectComponentPool<TransformComponent> _transformPool = componentManager.GetDirectPool<TransformComponent>();
    private readonly DirectComponentPool<DisplayTextComponent> _displayTextPool = componentManager.GetDirectPool<DisplayTextComponent>();

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

        _hostWindow.TitleText = mapViewState.SelectedMapNodePosition is { } selected
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

        if (mapViewState.SelectedMapNodePosition is not { } selected)
        {
            return;
        }

        // SelectedMapNodePosition is a plain settable property; MapWindow is the only
        // current writer and already validates on-map before setting it, but nothing
        // enforces that here, so guard directly against indexing out of bounds.
        if (!world.IsOnMap(new Engine.Math.Vector3Int(selected.X, selected.Y, 0)))
        {
            return;
        }

        // Scoped to MapViewState.CurrentMapLayer only -- the same layer MapWindow is actually
        // rendering -- rather than every layer, so the inspector shows what's on screen
        // instead of entities on layers currently hidden from view.
        var currentMapLayer = mapViewState.CurrentMapLayer;

        var blockingEntityId = world.Map.GetEntityId(new Engine.Math.Vector3Int(selected.X, selected.Y, currentMapLayer));
        if (blockingEntityId != -1)
        {
            _selectedEntityIds.Add(blockingEntityId);
        }

        // The terrain beneath the current layer (Flying has none) -- terrain is never a
        // Blocking creature-occupancy entity (see World.PlaceTerrainOnMap), so it lives in
        // Map's separate terrain store and has to be looked up independently of the Map slot
        // above.
        if (Map.TerrainLayerFor(currentMapLayer) is { } terrainLayer)
        {
            var terrainEntityId = world.Map.GetTerrainEntityId(selected.X, selected.Y, terrainLayer);
            if (terrainEntityId != -1)
            {
                _selectedEntityIds.Add(terrainEntityId);
            }
        }

        // Tiny/Phasing entities never occupy Map's Blocking slot (see World.IsBlocking), so
        // the checks above alone would silently drop them from the debug panel -- cross-check
        // the (small, sparse) Occupancy pool directly against the selected XY, filtered to
        // the current layer the same way the Blocking check above is.
        foreach (var entityId in _occupancyPool.EntityIds)
        {
            if (!_transformPool.Has(entityId))
            {
                continue;
            }

            var position = _transformPool.GetReadonly(entityId).Position;
            if (position.X == selected.X && position.Y == selected.Y && position.Z == currentMapLayer)
            {
                _selectedEntityIds.Add(entityId);
            }
        }
    }

    private List<TextWindow> CreateDebugWindowsForEntity(int entityId)
    {
        var createdWindows = new List<TextWindow>();

        if (_displayTextPool.Has(entityId))
        {
            // Bordered, and thicker than a component window's border (BorderSize 2 vs 1) --
            // this is the only visual break between one entity's block of component windows
            // and the next entity's, since child windows otherwise tile with nothing between
            // them. Without it, two adjacent entities' component lists read as one continuous
            // list with no indication where one entity ends and the next begins.
            var nameWindow = windowService.CreateWindow<TextWindow>(_hostWindow, new WindowOptions
            {
                Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = false },
                // Height uncapped (a generous, effectively-unlimited sentinel, not
                // _hostWindow.ContentSize.Y) -- selectionWindow itself is the thing that
                // scrolls now (CanUserScrollVertical, see GameShellBootstrapper), so each
                // component window should always render its full natural height rather than
                // getting clamped for "running out of room" the moment it's tiled past
                // selectionWindow's own fixed, one-screen-tall content size. Width still capped
                // to the host's content width -- that's the word-wrap boundary, unrelated to
                // the vertical scrolling concern.
                Layout = new WindowLayoutOptions { MaximumSize = new Vector2(_hostWindow.ContentSize.X, UnboundedChildHeight), DisplayMode = WindowDisplayMode.WrapContent },
                Chrome = new WindowChromeOptions { ShowTitle = false, ShowBorder = true, BorderSize = new Vector2(2, 2) },
                Text = new TextOptions { Text = _displayTextPool.GetReadonly(entityId).Name },
            });
            _hostWindow.AddChildWindow(nameWindow);
            createdWindows.Add(nameWindow);
        }

        _reusableInspectionList.Clear();
        componentInspector.CopyInspectionDataForEntity(entityId, _reusableInspectionList);

        foreach (var entry in _reusableInspectionList)
        {
            // MaximumSize.Y uncapped -- see the matching comment on nameWindow above.
            var componentWindow = windowService.CreateWindow<TextWindow>(_hostWindow, new WindowOptions
            {
                Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = false },
                Layout = new WindowLayoutOptions { MaximumSize = new Vector2(_hostWindow.ContentSize.X, UnboundedChildHeight), DisplayMode = WindowDisplayMode.WrapContent },
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

        if (_displayTextPool.Has(entityId) && windowIndex < windows.Count)
        {
            windows[windowIndex].UpdateText(_displayTextPool.GetReadonly(entityId).Name);
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
