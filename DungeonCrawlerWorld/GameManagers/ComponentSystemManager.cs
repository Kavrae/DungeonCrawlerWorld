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

        private EnergyRechargeSystem energyRechargeSystem;
        private HealthRegenSystem healthRegenSystem;
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
            energyRechargeSystem = new EnergyRechargeSystem();
            healthRegenSystem = new HealthRegenSystem();
            movementSystem = new MovementSystem();

            spriteBatchService = GameServices.GetService<SpriteBatchService>();
        }

        public void LoadContent() { }
        public void UnloadContent() { }

        //Update game components every x frames as defined by their FramesPerUpdate.
        //Sequentially increment update checks by 1 frame so that not all updates happen on the same commonly divisible frames
        public void Update(GameTime gameTime, GameVariables gameVariables)
        {
            //TODO which of these is taking the most time? Low updates per second
            if (currentFrame % energyRechargeSystem.FramesPerUpdate == 0)
            {
                energyRechargeSystem.Update(gameTime);
            }
            if ( (currentFrame + 1) % energyRechargeSystem.FramesPerUpdate == 0)
            {
                movementSystem.Update(gameTime);
            }
            if ( (currentFrame + 2) % healthRegenSystem.FramesPerUpdate == 0)
            {
                healthRegenSystem.Update(gameTime);
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
