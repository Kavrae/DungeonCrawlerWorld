using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Data;

namespace DungeonCrawlerWorld
{
    public interface IGameManager
    {
        public bool CanUpdateWhilePaused { get; }
        public void Initialize();
        public void LoadContent();
        public void UnloadContent();
        public void Update(GameTime gameTime, GameVariables gameVariables);
        public void Draw(GameTime gameTime);
    }
}
