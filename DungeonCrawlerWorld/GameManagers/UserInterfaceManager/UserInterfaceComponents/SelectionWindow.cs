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

        private MapNode[] selectedMapNodes;

        public SelectionWindow(World world, WindowOptions windowOptions) : base(null, windowOptions)
        {
            this.world = world;
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
            selectedMapNodes = world.SelectedMapNodes;
            if (selectedMapNodes != null && selectedMapNodes.Length > 0)
            {
                TitleText = $"Selected Map Node : {selectedMapNodes[0].Position.X},{selectedMapNodes[0].Position.Y}";

                _childWindows = new List<Window>();
                foreach (var mapNode in selectedMapNodes.Where(mapNode => mapNode.EntityId != null))
                {
                    if (world._GameVariables.IsDebugMode)
                    {
                        CreateComponentDebugInfoWindows(mapNode);
                    }
                    else
                    {
                        //TODO Player's selection view
                    }
                }
            }
            else
            {
                TitleText = "No map nodes selected";
            }

            base.Update(gameTime);
        }

        public void CreateComponentDebugInfoWindows(MapNode mapNode)
        {
            _childWindows = new List<Window>();

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
