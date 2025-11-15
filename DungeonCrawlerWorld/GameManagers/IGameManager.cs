using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld.GameManagers
{
    /// <summary>
    /// Interface defining the contract for game managers.
    /// </summary>
    public interface IGameManager
    {
        /// <summary>
        /// Indicates whether the game manager can update while the game is paused.
        /// This allows UserInterface components, debugging components, and user input to continue to run while the game is paused.
        /// </summary>
        public bool CanUpdateWhilePaused { get; }
        public void Initialize();
        public void LoadContent();
        public void UnloadContent();
        public void Update(GameTime gameTime, GameVariables gameVariables);
        public void Draw(GameTime gameTime);
    }
}
