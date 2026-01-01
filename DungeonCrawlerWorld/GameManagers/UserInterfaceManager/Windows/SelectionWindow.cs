using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class SelectionWindow : Window
    {
        private readonly World world;

        private List<MapNode> selectedMapNodes;

        public SelectionWindow(World world, WindowOptions windowOptions) : base(null, windowOptions)
        {
            this.world = world;
            selectedMapNodes = [];
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
            _childWindows.Clear();
            selectedMapNodes.Clear();

            if (world.SelectedMapNodePosition != null)
            {
                TitleText = $"Selected Map Node : {world.SelectedMapNodePosition.Value.X},{world.SelectedMapNodePosition.Value.Y}";

                for (var z = world.Map.Size.Z - 1; z >= 0; z--)
                {
                    selectedMapNodes.Add(world.Map.MapNodes[world.SelectedMapNodePosition.Value.X, world.SelectedMapNodePosition.Value.Y, z]);
                }

                if (_gameVariables.IsDebugMode)
                {
                    foreach (var selectedMapNode in selectedMapNodes)
                    {
                        if (selectedMapNode.EntityId != null)
                        {
                            CreateComponentDebugInfoWindows(selectedMapNode.EntityId.Value);
                        }
                    }
                }
                else
                {
                    //TODO Player's selection view
                }
            }
            else
            {
                TitleText = "No map nodes selected";
            }

            base.Update(gameTime);
        }

        public void CreateComponentDebugInfoWindows(int entityId)
        {
            var displayTextComponent = ComponentRepo.DisplayTextComponents[entityId];
            if (displayTextComponent != null)
            {
                AddChildWindow(new TextWindow(this, new TextWindowOptions
                {
                    ShowTitle = false,
                    Text = displayTextComponent.Value.Name,
                    ShowBorder = false,
                    MaximumSize = _contentSize,
                    DisplayMode = WindowDisplayMode.Grow
                }));
            }
            var components = ComponentRepo.GetAllComponents(entityId);
            for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                var component = components[componentIndex];
                var properties = component.GetType().GetProperties()
                    .Where(property => property.GetGetMethod().GetParameters().Length == 0);

                var text = new StringBuilder();
                for (var propertyIndex = 0; propertyIndex < properties.Count(); propertyIndex++)
                {
                    var propertyInfo = properties.ElementAt(propertyIndex);
                    text.Append("    ");
                    text.Append(propertyInfo.Name);
                    text.Append(" : ");
                    text.Append(propertyInfo.GetValue(component, null));

                    if (propertyIndex < properties.Count() - 1)
                    {
                        text.Append(Environment.NewLine);
                    }
                }

                AddChildWindow(new TextWindow(this, new TextWindowOptions
                {
                    ShowTitle = true,
                    TitleText = component.GetType().Name,
                    ShowBorder = true,
                    BorderSize = new Vector2(1, 1),
                    Text = text.ToString(),
                    MaximumSize = _contentSize,
                    DisplayMode = WindowDisplayMode.Grow
                }));
            }
        }
    }
}
