using System.Collections.Generic;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.GameComponents.DisplayComponent;
using DungeonCrawlerWorld.GameComponents.EntityFactoryComponent;
using DungeonCrawlerWorld.GameComponents.EntityManagerComponent;
using DungeonCrawlerWorld.GameComponents.GlobalInputComponent;
using DungeonCrawlerWorld.GameComponents.MapBuilderComponent;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld
{
    //TODO next feature :
    //  Create a set of ActionEvent types for each known Action that can be taken
    //  Each Entity has an ActionEvents list
    //  Entities and events add ActionEvents to the entities that they affect (ex: AttackEvent added to a target)
    //  Each component of an entity can act on the ActionEvents in the list. (ex: Attack event. Buffs reduce the damage. Health takes the damage. Achievements get triggered.
    //  ActionEvents are cleared out at the end of the update.
    //  ActionEvents need callbacks that can affect the entity in question. Ex: apply "charm"
    //      What if they're immune to charm? Or just resistant?  Need a way for that to cancel out or be reduced without needing a bunch of IF checks on every action.

    //Alternative. Entities have OnAttacked handlers specifically.
    //  Attacks have a list of effects
    //  Entities have OnX for each effect.  OnPoisoned, OnParalyzed, etc.
    //  This means double-implementing each thing. but it would mean buffs and de-buffs are easier to apply.
    //  Less versatile though. And would need to know about each module added to the entity. Can't just take away the HP module, as references would break.

    //Mix the two?
    //  Each module contains the OnYxz handlers that it cares about.
    //  Iterate over each module, sending it the attack (or whatever the event is)
    //  Maybe the module can have an IAttacked interface so I can only call the relevant ones?
    //      That will be a LOT of interfaces. But maybe worth it?
    public class GameLoop : Game
    {
        public readonly Game Game;

        private GraphicsDeviceManager _graphics;
        private DataAccess _dataAccess;

        private List<IGameComponent> _gameComponents;

        public GameLoop()
        {
            Game = this;

            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferHeight = 1050,
                PreferredBackBufferWidth = 1850
            };

            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            RegisterServices();
            CreateGameComponents();

            foreach (var component in _gameComponents)
            {
                component.Initialize();
            }

            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.Connect();

          base.Initialize();
        }

        protected override void LoadContent()
        {
            foreach (var component in _gameComponents)
            {
                component.LoadContent();
            }
            base.LoadContent();
        }

        protected override void UnloadContent()
        {
            foreach (var component in _gameComponents)
            {
                component.UnloadContent();
            }
            base.UnloadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            var gameVariables = _dataAccess.RetrieveGameVariables();

            foreach (var component in _gameComponents)
            {
                if( !gameVariables.IsPaused || component.CanUpdateWhilePaused)
                {
                    component.Update(gameTime);
                }
            }

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.White);

            foreach (var component in _gameComponents)
            {
                component.Draw(gameTime);
            }

            base.Draw(gameTime);
        }

        public void RegisterServices()
        {
            GameServices.AddService(_graphics.GraphicsDevice);
            GameServices.AddService(new DataAccessService());
            GameServices.AddService(new SpriteBatchService(_graphics.GraphicsDevice));
            GameServices.AddService(new FontService(Content));
        }

        public void CreateGameComponents()
        {
            _gameComponents = new List<IGameComponent>
            {
                new GlobalInputComponent(),
                new MapBuilderComponent(),
                new EntityEventManagerComponent(),
                new EntityFactoryComponent(),
                new DisplayComponent()
            };
        }
    }
}
