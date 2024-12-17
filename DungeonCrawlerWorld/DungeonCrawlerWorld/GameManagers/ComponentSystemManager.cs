using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.ComponentSystems;
using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameSystems
{
    /// <summary>
    /// Component System Manager
    /// Call Component Systems in a specified order.
    /// </summary>
    /// <todo>
    /// Configuration file to set the order of the systems via sequence number.
    /// </todo>
    public class ComponentSystemManager : IGameManager
    {
        private EnergyRechargeSystem actionEnergySystem;
        private MovementSystem movementSystem;

        private SpriteBatchService spriteBatchService;
        private SpriteBatch spriteBatch;

        public bool CanUpdateWhilePaused { get { return false; } }

        public ComponentSystemManager()
        {
        }

        public void Initialize()
        {
            actionEnergySystem = new EnergyRechargeSystem();
            movementSystem = new MovementSystem();

            spriteBatchService = GameServices.GetService<SpriteBatchService>();
        }

        public void LoadContent() { }
        public void UnloadContent() { }

        public void Update(GameTime gameTime, GameVariables gameVariables)
        {
            actionEnergySystem.Update(gameTime);
            movementSystem.Update(gameTime);
        }

        public void Draw(GameTime gameTime) { }
    }
}
