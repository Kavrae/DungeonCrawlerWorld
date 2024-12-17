using System.Collections.Generic;

using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Actions;
using System.Linq;

namespace DungeonCrawlerWorld.GameManagers.EntityEventManager
{ 
    //TODO implement event system

    public class EntityEventManager : IGameManager
    {
        private World _dataAccess;
        private Queue<GameEvent> gameEventQueue;

        public bool CanUpdateWhilePaused { get { return false; } }

        public EntityEventManager()
        {
            gameEventQueue = new Queue<GameEvent>();
        }

        public void Draw(GameTime gameTime) { }

        public void Initialize()
        {
            var dataAccessService = GameServices.GetService<DataAccessService>();
            _dataAccess = dataAccessService.RetrieveWorld();
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
        public void Update(GameTime gameTime, GameVariables gameVariables)
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
                    foreach(var targetEntity in action.TargetEntityIds)
                    {
                    }
                }
            }
        }

        public void ProcessEntities(GameTime gameTime)
        {
            /*var entities = _dataAccess.RetrieveEntities();
            foreach (var entity in entities)
            {
                entity.Update(gameTime);
            }
            */
        }
    }
}
