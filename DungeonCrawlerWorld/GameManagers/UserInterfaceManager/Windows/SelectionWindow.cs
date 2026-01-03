using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class SelectionWindow : Window
    {
        private readonly World world;
        private DataAccessService dataAccessService;
        private WindowService windowService;

        private List<MapNode> selectedMapNodes;

        private readonly Dictionary<int, List<TextWindow>> _entityDebugWindows = [];
        private readonly HashSet<int> _visibleDebugEntityIds = [];
        private readonly Dictionary<Type, PropertyInfo[]> _propertyCache = [];

        private readonly HashSet<int> _selectedEntityIds = [];
        private readonly StringBuilder _sharedStringBuilder = new();
        private TextWindowOptions _reusableTextOptions;

        private int _updatesSinceLastComponentRefresh = 0;
        private const int ComponentRefreshInterval = 10; // Most components update every 10 frames, so more frequent updates are a waste of processing

        public SelectionWindow() : base()
        {
            dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();

            windowService = GameServices.GetService<WindowService>();
            selectedMapNodes = [];
        }

        public override void BuildWindow(Window parentWindow, WindowOptions windowOptions)
        {
            base.BuildWindow(parentWindow, windowOptions);

            _reusableTextOptions = new TextWindowOptions
            {
                ShowTitle = false,
                ShowBorder = false,
                MaximumSize = _contentSize,
                DisplayMode = WindowDisplayMode.Grow
            };
        }

        public override void Initialize()
        {
            base.Initialize();
        }

        public override void LoadContent()
        {
            base.LoadContent();
        }

        public override void Update(GameTime gameTime)
        {
            selectedMapNodes.Clear();

            if (world.SelectedMapNodePosition != null)
            {
                for (var z = world.Map.Size.Z - 1; z >= 0; z--)
                {
                    selectedMapNodes.Add(world.Map.MapNodes[world.SelectedMapNodePosition.Value.X, world.SelectedMapNodePosition.Value.Y, z]);
                }
            }

            _selectedEntityIds.Clear();
            foreach (var selectedMapNode in selectedMapNodes)
            {
                if (selectedMapNode.EntityId != null)
                {
                    _selectedEntityIds.Add(selectedMapNode.EntityId.Value);
                }
            }

            var entityIdsToRemoveWindowsFor = _visibleDebugEntityIds.Except(_selectedEntityIds);
            foreach (var entityId in entityIdsToRemoveWindowsFor)
            {
                if (_entityDebugWindows.TryGetValue(entityId, out var windows))
                {
                    foreach (var window in windows)
                    {
                        window.Close();
                    }
                    _entityDebugWindows.Remove(entityId);
                }
                _visibleDebugEntityIds.Remove(entityId);
            }

            var entityIdsToAddWindowsFor = _selectedEntityIds.Except(_visibleDebugEntityIds);
            foreach (var entityId in entityIdsToAddWindowsFor)
            {
                var newWindows = CreateDebugWindowsForEntity(entityId);
                if (newWindows != null && newWindows.Count > 0)
                {
                    _entityDebugWindows[entityId] = newWindows;
                    _visibleDebugEntityIds.Add(entityId);
                }
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

            if (world.SelectedMapNodePosition != null)
            {
                TitleText = $"Selected Map Node : {world.SelectedMapNodePosition.Value.X},{world.SelectedMapNodePosition.Value.Y}";
            }
            else
            {
                TitleText = "No map nodes selected";
            }

            base.Update(gameTime);
        }

        private List<TextWindow> CreateDebugWindowsForEntity(int entityId)
        {
            var createdWindows = new List<TextWindow>();

            var displayTextComponent = ComponentRepo.DisplayTextComponents[entityId];
            if (displayTextComponent != null)
            {
                _reusableTextOptions.ShowTitle = false;
                _reusableTextOptions.Text = displayTextComponent.Value.Name;
                _reusableTextOptions.ShowBorder = false;
                _reusableTextOptions.MaximumSize = _contentSize;
                _reusableTextOptions.DisplayMode = WindowDisplayMode.Grow;

                var nameWindow = windowService.CreateWindow<TextWindow, TextWindowOptions>(this, _reusableTextOptions);
                AddChildWindow(nameWindow);
                createdWindows.Add(nameWindow);
            }

            _reusableTextOptions.ShowTitle = true;
            _reusableTextOptions.ShowBorder = true;
            _reusableTextOptions.BorderSize = new Vector2(1, 1);
            _reusableTextOptions.MaximumSize = _contentSize;
            _reusableTextOptions.DisplayMode = WindowDisplayMode.Grow;

            var components = ComponentRepo.GetAllComponents(entityId);
            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                var component = components[componentIndex];

                _reusableTextOptions.TitleText = component.GetType().Name;
                _reusableTextOptions.Text = BuildComponentText(component);

                var componentWindow = windowService.CreateWindow<TextWindow, TextWindowOptions>(this, _reusableTextOptions);

                AddChildWindow(componentWindow);
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

            var components = ComponentRepo.GetAllComponents(entityId);

            int windowIndex = 0;
            var displayTextComponent = ComponentRepo.DisplayTextComponents[entityId];
            if (displayTextComponent != null)
            {
                windows[0].UpdateText(displayTextComponent.Value.Name);
                windowIndex++;
            }

            for (var componentIndex = 0; componentIndex < components.Count && windowIndex < windows.Count; componentIndex++, windowIndex++)
            {
                var component = components[componentIndex];
                var text = BuildComponentText(component);
                windows[windowIndex].UpdateText(text);
            }
        }

        private string BuildComponentText(IEntityComponent component)
        {
            _sharedStringBuilder.Clear();

            var type = component.GetType();
            if (!_propertyCache.TryGetValue(type, out var properties))
            {
                properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetGetMethod() != null && p.GetGetMethod().GetParameters().Length == 0)
                    .ToArray();
                _propertyCache[type] = properties;
            }

            for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
            {
                var property = properties[propertyIndex];
                _sharedStringBuilder.Append("    ");
                _sharedStringBuilder.Append(property.Name);
                _sharedStringBuilder.Append(" : ");
                var value = property.GetValue(component, null);
                _sharedStringBuilder.Append(value);
                if (propertyIndex < properties.Length - 1)
                {
                    _sharedStringBuilder.Append(Environment.NewLine);
                }
            }

            return _sharedStringBuilder.ToString();
        }
    }
}