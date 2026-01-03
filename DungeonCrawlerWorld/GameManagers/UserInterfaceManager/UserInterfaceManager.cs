using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{

    /// <summary>
    /// Manages the user interface components of the game.
    /// </summary>
    public class UserInterfaceManager : IGameManager
    {
        /// <summary>
        /// Indicates that the User Interface Manager can update while the game is paused.
        /// If this value is turned off, the screen will remain static while paused, preventing user input and UI updates.
        /// </summary>
        public bool CanUpdateWhilePaused => true;

        private GraphicsDevice graphicsDevice;
        private SpriteBatchService spriteBatchService;
        private DataAccessService dataAccessService;
        private WindowService windowService;

        private DebugWindow debugWindow;
        private MapWindow mapWindow;
        private SelectionWindow selectionWindow;

        /// <summary>
        /// A simple 1x1 pixel white rectangle texture used for drawing UI elements.
        /// Improves performance by copying a common texture instead of creating new textures for each element.
        /// </summary>
        private Texture2D unitRectangle;

        /// <summary>
        /// All UI elements are contained within user interface windows.
        /// This allows each element to be easily repositioned, resized, drawn, and updated based on their settings.
        /// </summary>
        private List<Window> userInterfaceWindows;

        /// <summary>
        /// Updated each frame, this allows for comparison of current and previous keyboard states to detect key presses.
        /// </summary>
        private KeyboardState previousKeyboardState;

        public void Initialize()
        {
            dataAccessService = GameServices.GetService<DataAccessService>();

            graphicsDevice = GameServices.GetService<GraphicsDevice>();

            spriteBatchService = GameServices.GetService<SpriteBatchService>();

            windowService = GameServices.GetService<WindowService>();

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

        /// <summary>
        /// Creates and initializes all root user interface windows.
        /// All other windows are children of these root windows.
        /// Update and draw order are determined by the order of windows in the userInterfaceWindows list.
        /// </summary>
        /// <todo>
        /// Make default window creation values configurable via a settings file.
        /// </todo>
        public void CreateUserInterfaceWindows()
        {
            debugWindow = windowService.CreateWindow<DebugWindow, WindowOptions>(
                null,
                new WindowOptions
                {
                    RelativePosition = new Vector2(10, 0),
                    Size = new Vector2(1536, 20),
                    TitleText = "Debug Window",
                    DisplayMode = WindowDisplayMode.Static,
                });

            mapWindow = windowService.CreateWindow<MapWindow, WindowOptions>(
                null,
                new WindowOptions
                {
                    RelativePosition = new Vector2(12, 12),
                    Size = new Vector2(1536, 930),
                    ShowBorder = false,
                    ShowTitle = true,
                    TitleText = "Dungeon Crawler World : Test Floor",
                    DisplayMode = WindowDisplayMode.Static
                });

            selectionWindow = windowService.CreateWindow<SelectionWindow, WindowOptions>(
                null,
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

            userInterfaceWindows =
            [
                debugWindow,
                mapWindow,
                selectionWindow
            ];
        }

        /// <summary>
        /// Handles user input for the User Interface Manager.
        /// Input is handled via a recursive pattern in which each layer checks the click position against the display rectangle of each child window
        /// until a match is found. Each child window will do the same for its children until the deepest child window is found. That component
        /// will then run its Header or Content click handler.
        /// </summary>
        /// <todo>
        /// Recursive pattern to determine the correct window to handle keyboard actions. Move UpdateScrollPosition to the mapWindow's input handler.
        /// Keymapping via config file
        /// </todo>
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

                //TODO change to generic mouse handling
                var mouseState = Mouse.GetState();
                if (mouseState.LeftButton == ButtonState.Pressed)
                {
                    foreach (var userInterfaceWindow in userInterfaceWindows)
                    {
                        if (userInterfaceWindow.IsInDisplayRectangle(mouseState.X, mouseState.Y))
                        {
                            userInterfaceWindow.HandleClick(new Point(mouseState.X, mouseState.Y));
                            break;
                        }
                    }
                }
            }

            previousKeyboardState = currentKeyboardState;
        }

        /// <summary>
        /// Toggles the paused state of the user based on the current state in gameVariables
        /// By centralizing the isUserPaused variable, multiple managers and systems can pause the game. 
        /// Ex : Unskippable system notifications.
        /// </summary>
        private void ToggleIsUserPaused()
        {
            var gameVariables = dataAccessService.RetrieveGameVariables();
            gameVariables.IsUserPaused = !gameVariables.IsUserPaused;
        }
    }
}
