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
    ElementPoolService elementPoolService) : IElementContent
{
    private const int ComponentRefreshInterval = 10; // Most components update every 10 frames, so more frequent refreshes are wasted work.

    /// <summary>A generous, effectively-unlimited per-component-window height cap -- see CreateDebugWindowsForEntity.</summary>
    private const float UnboundedChildHeight = 10000f;

    // Resolved once and reused rather than re-resolved via ComponentManager's dictionary
    // lookup on every call -- CreateDebugWindowsForEntity/RefreshDebugWindowsForEntity run
    // every frame a selection is visible, so this was otherwise being looked up 60 times a
    // second for no reason. Matches the pattern MapWindow already uses for its own pool references.
    private readonly DirectComponentPool<DisplayTextComponent> _displayTextPool = componentManager.GetDirectPool<DisplayTextComponent>();

    private readonly Dictionary<int, List<TextWindow>> _entityDebugWindows = [];
    private readonly HashSet<int> _visibleDebugEntityIds = [];
    private readonly HashSet<int> _selectedEntityIds = [];
    private readonly List<InspectedComponentEntry> _reusableInspectionList = [];
    private readonly List<int> _reusableDiffBuffer = [];

    private Window _hostWindow = null!;
    private int _updatesSinceLastComponentRefresh;
    private Point? _titleTextSourcePosition;
    private bool _hasSetTitleText;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
    }

    public void Update(GameTime gameTime)
    {
        RecomputeSelectedEntityIds();

        // Manual diff into a reused buffer rather than HashSet.Except().ToArray() -- this runs
        // every frame regardless of whether the selection changed, so the two LINQ allocations
        // (Except's internal set plus ToArray's array) would otherwise be permanent per-frame
        // garbage even on the common "nothing selected" frame. ToArray-equivalent snapshotting
        // is still needed since the loop bodies mutate _visibleDebugEntityIds/_selectedEntityIds
        // while iterating.
        _reusableDiffBuffer.Clear();
        foreach (var entityId in _visibleDebugEntityIds)
        {
            if (!_selectedEntityIds.Contains(entityId))
            {
                _reusableDiffBuffer.Add(entityId);
            }
        }

        foreach (var entityId in _reusableDiffBuffer)
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

        _reusableDiffBuffer.Clear();
        foreach (var entityId in _selectedEntityIds)
        {
            if (!_visibleDebugEntityIds.Contains(entityId))
            {
                _reusableDiffBuffer.Add(entityId);
            }
        }

        foreach (var entityId in _reusableDiffBuffer)
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

        // TitleText only actually needs rebuilding when the selection changes -- most frames
        // it's identical to last frame's value, so this guard avoids a steady-state per-frame
        // string allocation (unlike DebugWindowContent's rate counters, this isn't a live
        // display that needs to change every frame regardless).
        var currentSelection = mapViewState.SelectedMapNodePosition;
        if (!_hasSetTitleText || currentSelection != _titleTextSourcePosition)
        {
            _hasSetTitleText = true;
            _titleTextSourcePosition = currentSelection;
            _hostWindow.TitleText = currentSelection is { } selected
                ? $"Selected Map Node : {selected.X},{selected.Y}"
                : "No map nodes selected";
        }
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

        var blockingEntityId = world.Map.GetBlockingEntityId(new Engine.Math.Vector3Int(selected.X, selected.Y, currentMapLayer));
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
        // the check above alone would silently drop them from the debug panel -- the
        // position-keyed non-Blocking index (World.GetNonBlockingEntityIdsAt) answers exactly
        // that in O(entities actually here) instead of a full-population scan.
        foreach (var entityId in world.GetNonBlockingEntityIdsAt(new Engine.Math.Vector3Int(selected.X, selected.Y, currentMapLayer)))
        {
            _selectedEntityIds.Add(entityId);
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
            var nameWindow = elementPoolService.CreateElement<TextWindow>(_hostWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                // Height uncapped (a generous, effectively-unlimited sentinel, not
                // _hostWindow.ContentSize.Y) -- selectionWindow itself is the thing that
                // scrolls now (CanUserScrollVertical, see GameShellBootstrapper), so each
                // component window should always render its full natural height rather than
                // getting clamped for "running out of room" the moment it's tiled past
                // selectionWindow's own fixed, one-screen-tall content size. Width still capped
                // to the host's content width -- that's the word-wrap boundary, unrelated to
                // the vertical scrolling concern.
                Layout = new ElementLayoutOptions { MaximumSize = new Vector2(_hostWindow.ContentSize.X, UnboundedChildHeight), DisplayMode = ElementDisplayMode.WrapContent },
                Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = true, BorderSize = new Vector2(2, 2) },
                Text = new TextOptions { Text = _displayTextPool.GetReadonly(entityId).Name },
            });
            _hostWindow.AddChild(nameWindow);
            createdWindows.Add(nameWindow);
        }

        _reusableInspectionList.Clear();
        componentInspector.CopyInspectionDataForEntity(entityId, _reusableInspectionList);

        foreach (var entry in _reusableInspectionList)
        {
            // MaximumSize.Y uncapped -- see the matching comment on nameWindow above.
            var componentWindow = elementPoolService.CreateElement<TextWindow>(_hostWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { MaximumSize = new Vector2(_hostWindow.ContentSize.X, UnboundedChildHeight), DisplayMode = ElementDisplayMode.WrapContent },
                Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = entry.ComponentType.Name, ShowBorder = true, BorderSize = new Vector2(1, 1) },
                Text = new TextOptions { Text = entry.Value },
            });
            _hostWindow.AddChild(componentWindow);
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

            windows[windowIndex].UpdateText(entry.Value);
            windowIndex++;
        }
    }
}