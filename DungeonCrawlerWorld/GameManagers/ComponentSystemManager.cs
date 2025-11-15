using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.ComponentSystems;
using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameManagers.ComponentSystemManager
{
    /// <summary>
    /// Component System Manager to update Component Systems in a specified order.
    /// </summary>
    /// <todo>
    /// Configuration file to set the order of the systems via sequence number.
    /// </todo>
    public class ComponentSystemManager : IGameManager
    {
        /// <summary>
        /// Prevents all component systems from updating while the game is paused.
        /// This is safer than setting the value on each component system individually.
        /// </summary>
        public bool CanUpdateWhilePaused => false;

        /// <summary>
        /// The current frame of a frame cycle defined by the framesPerCycle value.
        /// Used to stagger component system updates within the frame cycle.
        /// </summary>
        private byte currentFrame;

        /// <summary>
        /// The number of frames in a complete update cycle for all component systems.
        /// Updates for a component can occur multiple times to cycle.
        /// </summary>
        private byte framesPerCycle;

        private EnergyRechargeSystem energyRechargeSystem;
        private HealthRegenSystem healthRegenSystem;
        private MovementSystem movementSystem;


        public ComponentSystemManager()
        {
        }

        /// <summary>
        /// Initializes the component systems and sets the frame cycle length.
        /// </summary>
        /// <todo>
        /// Make the frame cycle length configurable via a settings file.
        /// Dynamically load and initialize component systems via reflection so modding can add additional systems and reduce boilerplate.
        /// </todo>
        public void Initialize()
        {
            framesPerCycle = 30;
            energyRechargeSystem = new EnergyRechargeSystem();
            healthRegenSystem = new HealthRegenSystem();
            movementSystem = new MovementSystem();
        }

        public void LoadContent() { }
        public void UnloadContent() { }

        /// <summary>
        /// Updates the component systems in a specified order based on the current frame and their update intervals.
        /// By offsetting the currentFrame, the updates are staggered to avoid performance spikes.
        /// </summary>
        /// <todo>
        /// Make the update intervals configurable via a settings file.
        /// Make the update sequence configurable via a settings file.
        /// </todo>
        public void Update(GameTime gameTime, GameVariables gameVariables)
        {
            if (currentFrame % energyRechargeSystem.FramesPerUpdate == 0)
            {
                energyRechargeSystem.Update(gameTime);
            }
            if ((currentFrame + 1) % energyRechargeSystem.FramesPerUpdate == 0)
            {
                movementSystem.Update(gameTime);
            }
            if ((currentFrame + 2) % healthRegenSystem.FramesPerUpdate == 0)
            {
                healthRegenSystem.Update(gameTime);
            }

            currentFrame++;
            if (currentFrame == framesPerCycle)
            {
                currentFrame = 0;
            }
        }

        /// <summary>
        /// Components should NEVER be drawn. Drawing should only be done from the UserInterfaceManager.
        /// </summary>
        public void Draw(GameTime gameTime) { }
    }
}
