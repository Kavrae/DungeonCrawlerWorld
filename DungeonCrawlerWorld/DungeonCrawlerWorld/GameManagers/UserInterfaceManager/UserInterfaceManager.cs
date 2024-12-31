using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class UserInterfaceManager : IGameManager
    {
        public bool CanUpdateWhilePaused => true;

        private World world;
        private GraphicsDevice graphicsDevice;
        private SpriteBatchService spriteBatchService;

        private DebugDisplay debugDisplay;
        private MapDisplay mapDisplay;
        private SelectionDisplay selectionDisplay;

        private Texture2D unitRectangle;

        private List<UserInterfaceComponent> displayComponents;

        private KeyboardState previousKeyboardState;

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();

            graphicsDevice = GameServices.GetService<GraphicsDevice>();

            spriteBatchService = GameServices.GetService<SpriteBatchService>();

            previousKeyboardState = Keyboard.GetState();

            unitRectangle = new Texture2D(graphicsDevice, 1, 1);
            unitRectangle.SetData(new[] { Color.White });

            CreateDisplayComponents();
            foreach (var displayComponent in displayComponents)
            {
                displayComponent.Initialize();
            }
        }

        public void LoadContent()
        {
            foreach (var displayComponent in displayComponents)
            {
                displayComponent.LoadContent();
            }
        }

        public void UnloadContent() { }

        public void Update( GameTime gameTime, GameVariables gameVariables)
        {
            HandleUserInput();

            foreach(var displayComponent in displayComponents)
            {
                displayComponent.Update(gameTime);
            }
        }

        public void Draw(GameTime gameTime)
        {
            var spriteBatch = spriteBatchService.StartSpriteBatch();

            foreach (var displayComponent in displayComponents)
            {
                //TODO These don't overlap. can each of these be drawn in a separate thread?
                displayComponent.Draw(gameTime, spriteBatch, unitRectangle);
            }

            spriteBatchService.EndSpriteBatch();
        }

        public void CreateDisplayComponents()
        {
            debugDisplay = new DebugDisplay(
                    world,
                    new Point(10, 0),
                    new Point(1440, 20));

            mapDisplay = new MapDisplay(
                    world,
                    new Point(10, 15),
                    new Point(1650, 920),
                    tileSize: new Point(12, 12));

            selectionDisplay = new SelectionDisplay(
                    world,
                    new Point(1670, 15),
                    new Point(170, 1440));

            displayComponents = new List<UserInterfaceComponent>
            {
                debugDisplay,
                mapDisplay,
                selectionDisplay
            };
        }

        private void HandleUserInput()
        {
            var inputMode = InputMode.Map;
            var currentKeyboardState = Keyboard.GetState();

            if (inputMode == InputMode.Map)
            {
                if ( currentKeyboardState.IsKeyDown(Keys.Space) && !previousKeyboardState.IsKeyDown(Keys.Space))
                {
                    world.ToggleIsPaused();
                }

                if (Keyboard.GetState().IsKeyDown(Keys.D))
                {
                    mapDisplay.UpdateScrollPosition(new Point(1,0));
                }
                if (Keyboard.GetState().IsKeyDown(Keys.A))
                {
                    mapDisplay.UpdateScrollPosition(new Point(-1, 0));
                }
                if (Keyboard.GetState().IsKeyDown(Keys.W))
                {
                    mapDisplay.UpdateScrollPosition(new Point(0, -1));
                }
                if (Keyboard.GetState().IsKeyDown(Keys.S))
                {
                    mapDisplay.UpdateScrollPosition(new Point(0, 1));
                }

                var mouseState = Mouse.GetState();
                if (mouseState.LeftButton == ButtonState.Pressed && mapDisplay.IsInDisplayRectangle(mouseState.Position))
                {
                    mapDisplay.SelectMapNodes(mouseState.Position);
                }
            }

            previousKeyboardState = currentKeyboardState;
        }
    }
}
