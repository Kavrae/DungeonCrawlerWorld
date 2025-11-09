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
        private DataAccessService dataAccessService;

        private DebugWindow debugWindow;
        private MapWindow mapWindow;
        private SelectionWindow selectionWindow;

        private Texture2D unitRectangle;

        private List<Window> userInterfaceWindows;

        private KeyboardState previousKeyboardState;

        public void Initialize()
        {
            dataAccessService = GameServices.GetService<DataAccessService>();
            world = dataAccessService.RetrieveWorld();

            graphicsDevice = GameServices.GetService<GraphicsDevice>();

            spriteBatchService = GameServices.GetService<SpriteBatchService>();

            previousKeyboardState = Keyboard.GetState();

            unitRectangle = new Texture2D(graphicsDevice, 1, 1);
            unitRectangle.SetData(new[] { Color.White });

            CreateUserInterfaceWindows();
            foreach (var userInterfaceWindow in userInterfaceWindows)
            {
                userInterfaceWindow.Initialize();
            }
        }

        public void LoadContent()
        {
            foreach (var userInterfaceWindow in userInterfaceWindows)
            {
                userInterfaceWindow.LoadContent();
            }
        }

        public void UnloadContent() { }

        public void Update(GameTime gameTime, GameVariables gameVariables)
        {
            HandleUserInput();

            foreach (var userInterfaceWindow in userInterfaceWindows)
            {
                userInterfaceWindow.Update(gameTime);
            }
        }

        public void Draw(GameTime gameTime)
        {
            var spriteBatch = spriteBatchService.StartSpriteBatch();

            foreach (var userInterfaceWindow in userInterfaceWindows)
            {
                userInterfaceWindow.Draw(gameTime, spriteBatch, unitRectangle);
            }

            spriteBatchService.EndSpriteBatch();
        }

        public void CreateUserInterfaceWindows()
        {
            debugWindow = new DebugWindow(
                    world,
                    new WindowOptions
                    {
                        RelativePosition = new Vector2(10, 0),
                        Size = new Vector2(1536, 20),
                        TitleText = "Debug Window",
                        DisplayMode = WindowDisplayMode.Static,
                    });

            mapWindow = new MapWindow(
                    world,
                    tileSize: new Point(12, 12),
                    new WindowOptions
                    {
                        RelativePosition = new Vector2(10, 15),
                        Size = new Vector2(1536, 930),
                        ShowBorder = false,
                        ShowTitle = true,
                        TitleText = "Dungeon Crawler World : Test Floor",
                        DisplayMode = WindowDisplayMode.Static
                    });

            selectionWindow = new SelectionWindow(
                    world,
                    new WindowOptions
                    {
                        RelativePosition = new Vector2(1565, 15),
                        Size = new Vector2(270, 1440),
                        ShowTitle = true,
                        TitleText = "No map nodes selected",
                        CanContainChildWindows = true,
                        ChildWindowTileMode = WindowTileMode.Vertical,
                        DisplayMode = WindowDisplayMode.Static
                    });

            userInterfaceWindows = new List<Window>
            {
                debugWindow,
                mapWindow,
                selectionWindow
            };
        }

        private void HandleUserInput()
        {
            var inputMode = InputMode.Map;
            var currentKeyboardState = Keyboard.GetState();

            if (inputMode == InputMode.Map)
            {
                if (currentKeyboardState.IsKeyDown(Keys.Space) && !previousKeyboardState.IsKeyDown(Keys.Space))
                {
                    ToggleIsUserPaused();
                }

                if (Keyboard.GetState().IsKeyDown(Keys.D))
                {
                    mapWindow.UpdateScrollPosition(new Point(1, 0));
                }
                if (Keyboard.GetState().IsKeyDown(Keys.A))
                {
                    mapWindow.UpdateScrollPosition(new Point(-1, 0));
                }
                if (Keyboard.GetState().IsKeyDown(Keys.W))
                {
                    mapWindow.UpdateScrollPosition(new Point(0, -1));
                }
                if (Keyboard.GetState().IsKeyDown(Keys.S))
                {
                    mapWindow.UpdateScrollPosition(new Point(0, 1));
                }

                var mouseState = Mouse.GetState();
                if (mouseState.LeftButton == ButtonState.Pressed)
                {
                    foreach (var userInterfaceWindow in userInterfaceWindows)
                    {
                        if (userInterfaceWindow.IsInDisplayRectangle(mouseState.Position))
                        {
                            userInterfaceWindow.HandleClickDown(mouseState.Position.ToVector2());
                            break;
                        }
                    }
                }
            }

            previousKeyboardState = currentKeyboardState;
        }

        private void ToggleIsUserPaused()
        {
            var gameVariables = dataAccessService.RetrieveGameVariables();
            gameVariables.IsUserPaused = !gameVariables.IsUserPaused;
        }
    }
}
