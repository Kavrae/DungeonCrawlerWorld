using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.GameManagers;
using DungeonCrawlerWorld.GameManagers.ComponentSystemManager;
using DungeonCrawlerWorld.GameManagers.MapBuilderManager;
using DungeonCrawlerWorld.GameManagers.EntityEventManager;
using DungeonCrawlerWorld.GameManagers.EntityFactoryManager;
using DungeonCrawlerWorld.GameManagers.UserInterfaceManager;
using DungeonCrawlerWorld.GameManagers.NotificationManager;
using DungeonCrawlerWorld.Components;
using System.Diagnostics;

namespace DungeonCrawlerWorld
{
    //TODO Redo MapHeight and movement based collision detection
    //TODO real map creation.
    //TODO Middle of screen popup for pausing and map loading.
    //Note : In UserInterfaceManager
    //Note2 : Has max width. Height based on wordwrap within that max width
    //Note3 : center based on size.
    //Note : arterial hallways are 30 feet wide. (34 including the walls)
    //          Side hallways at 14 feet wide (18 including the walls)
    //Note2 : each tile is 2 feet wide (human shoulders are 1.5 feet)
    //Note3 : restrooms every 0.25 miles (660 tiles)
    //Note4 : safe rooms every 2 miles. (5280 tiles)
    //Note5 : stairwells are as wide as the entire hallway
    //Note6 : tutorial guild after first fight, which unlocks full game for the crawler
    public class GameLoop : Game
    {
        public readonly Game Game;

        private GraphicsDeviceManager graphics;
        private DataAccessService dataAccessService;

        private List<IGameManager> updatableGameManagers;
        private List<IGameManager> drawableGameManagers;

        private Dictionary<int, MovementComponent> test1;
        private Utilities.SparseSet<MovementComponent> test2;

        public GameLoop()
        {
            Game = this;

            graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferHeight = 1050,
                PreferredBackBufferWidth = 1850,
            };

            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            RegisterServices();
            InitializeGameManagers();

            foreach (var component in updatableGameManagers)
            {
                component.Initialize();
            }

            dataAccessService = GameServices.GetService<DataAccessService>();

            base.Initialize();
        }

        protected override void LoadContent()
        {
            foreach (var component in updatableGameManagers)
            {
                component.LoadContent();
            }
            base.LoadContent();
        }

        protected override void UnloadContent()
        {
            foreach (var component in updatableGameManagers)
            {
                component.UnloadContent();
            }
            base.UnloadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            var gameVariables = dataAccessService.RetrieveGameVariables();

            foreach (var gameManager in updatableGameManagers)
            {
                if (!gameVariables.IsPaused || gameManager.CanUpdateWhilePaused)
                {
                    gameManager.Update(gameTime, gameVariables);
                }
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.White);

            foreach (var component in drawableGameManagers)
            {
                component.Draw(gameTime);
            }
        }

        public void RegisterServices()
        {
            GameServices.AddService(graphics.GraphicsDevice);
            GameServices.AddService(new DataAccessService());
            GameServices.AddService(new SpriteBatchService(graphics.GraphicsDevice));
            GameServices.AddService(new FontService(Content));
        }

        public void InitializeGameManagers()
        {
            var userInterfaceManager = new UserInterfaceManager();
            var notificationManager = new NotificationManager();
            updatableGameManagers = new List<IGameManager>
            {
                new ComponentSystemManager(),
                new EntityFactoryManager(),
                new MapBuilderManager(),
                new EntityEventManager(),
                userInterfaceManager,
                notificationManager
            };
            drawableGameManagers = new List<IGameManager>
            {
                userInterfaceManager,
                notificationManager
            };
        }
    }
}
