using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.ComponentSystems;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.ComponentSystemManager
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
        public bool CanUpdateWhilePaused => false;

        private EnergyRechargeSystem actionEnergySystem;
        private MovementSystem movementSystem;

        private SpriteBatchService spriteBatchService;
        private SpriteBatch spriteBatch;

        private byte currentFrame;
        private byte framesPerCycle;

        public ComponentSystemManager()
        {
        }

        public void Initialize()
        {
            framesPerCycle = 30;
            actionEnergySystem = new EnergyRechargeSystem();
            movementSystem = new MovementSystem();

            spriteBatchService = GameServices.GetService<SpriteBatchService>();
        }

        public void LoadContent() { }
        public void UnloadContent() { }

        //Update game components every x frames as defined by their FramesPerUpdate.
        //Sequentially increment update checks by 1 frame so that not all updates happen on the same commonly divisible frames
        public void Update(GameTime gameTime, GameVariables gameVariables)
        {
            if(currentFrame % actionEnergySystem.FramesPerUpdate == 0)
            {
                actionEnergySystem.Update(gameTime);
            }
            if ( (currentFrame + 1) % actionEnergySystem.FramesPerUpdate == 0)
            {
                movementSystem.Update(gameTime);
            }

            currentFrame++;
            if(currentFrame == framesPerCycle)
            {
                currentFrame = 0;
            }
        }

        public void Draw(GameTime gameTime) { }
    }
}
