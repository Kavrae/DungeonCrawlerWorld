using System.Collections.Generic;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Actions;
using System.Linq;

namespace DungeonCrawlerWorld.GameComponents.EntityManagerComponent
{ 
    //TODO swap to full ECS
    //Components are ONLY data
    //  Damage could be its own component that's added at runtime and then removed when action'd. Maybe this should be an Action, not a component?
    //Systems are only logic
    //Systems act on one component type at a time, updating ALL of them at once for that logic.
    //  Ex: DeathSystem handles death checks for everything at once.
    //Systems pull data as needed using getXyz methods that won't explode if they don't exist.
    //Systems send out events. Where sent to? how to consume??? EventSystem?
    //

    public class EntityEventManagerComponent : IGameComponent
    {
        private DataAccess _dataAccess;
        private Queue<GameEvent> gameEventQueue;

        public bool CanUpdateWhilePaused { get { return false; } }

        public EntityEventManagerComponent()
        {
            gameEventQueue = new Queue<GameEvent>();
        }

        public void Draw(GameTime gameTime) { }

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.Connect();
        }

        public void LoadContent()
        {
        }

        public void UnloadContent()
        {
        }

        //TODO if it's off the screen, only update once per second and do a "catchup" update. May be inaccurate, but close enough.
        //TODO if it's further away, do even longer update calls
        //TODO only update active entities
        public void Update(GameTime gameTime)
        {
            ProcessEvents(gameTime);
            ProcessEntities(gameTime);
            
        }

        public void ProcessEvents(GameTime gameTime)
        {
            gameEventQueue.OrderBy(gameEvent => gameEvent.Priority);

            while(gameEventQueue.Any())
            {
                var gameEvent = gameEventQueue.Dequeue();
                var actions = gameEvent.GetActions();

                foreach( var action in actions)
                {
                    foreach(var targetEntity in action.TargetEntities)
                    {
                    }
                }
            }
        }

        public void ProcessEntities(GameTime gameTime)
        {
            var entities = _dataAccess.RetrieveEntities();
            foreach (var entity in entities)
            {
                entity.Update(gameTime);
            }
        }
    }
}
