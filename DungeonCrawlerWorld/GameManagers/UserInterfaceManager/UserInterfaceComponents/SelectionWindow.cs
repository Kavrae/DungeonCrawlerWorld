using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class SelectionWindow : Window
    {
        private readonly World world;

        private List<MapNode> selectedMapNodes;

        public SelectionWindow(World world, WindowOptions windowOptions) : base(null, windowOptions)
        {
            this.world = world;
            selectedMapNodes = new List<MapNode>();
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
            _childWindows = new List<Window>();
            selectedMapNodes = new List<MapNode>();

            if (world.SelectedMapNodePosition != null)
            {
                TitleText = $"Selected Map Node : {world.SelectedMapNodePosition.Value.X},{world.SelectedMapNodePosition.Value.Y}";

                for (var z = world.Map.Size.Z - 1; z >= 0; z--)
                {
                    selectedMapNodes.Add(world.Map.MapNodes[world.SelectedMapNodePosition.Value.X, world.SelectedMapNodePosition.Value.Y, z]);
                }

                if (world._GameVariables.IsDebugMode)
                {
                    foreach (var selectedMapNode in selectedMapNodes.Where(mapNode => mapNode.EntityId != null))
                    {
                        CreateComponentDebugInfoWindows(selectedMapNode);
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
                selectedMapNodes = new List<MapNode>();
            }

            base.Update(gameTime);
        }

        public void CreateComponentDebugInfoWindows(MapNode mapNode)
        {
            foreach (var component in ComponentRepo.GetAllComponents(mapNode.EntityId.Value))
            {
                var text = new StringBuilder();
                foreach (PropertyInfo propertyInfo in component.GetType().GetProperties()
                    .Where(property => !property.GetGetMethod().GetParameters().Any()))
                {
                    text.Append($"    {propertyInfo.Name} : {propertyInfo.GetValue(component, null)}{Environment.NewLine}");
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

        public override void HandleTitleClickDown(Vector2 mousePosition)
        {
            //Does nothing
        }

        public override void HandleContentClickDown(Vector2 mousePosition)
        {
            //Does nothing
        }
    }
}
