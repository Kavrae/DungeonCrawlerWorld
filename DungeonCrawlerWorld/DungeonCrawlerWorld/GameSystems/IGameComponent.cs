using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld
{
    //TODO split into IGameComponent and IDrawableGameComponent. Same with entity components
    public interface IGameComponent
    {
        public bool CanUpdateWhilePaused { get; }
        public void Initialize();
        public void LoadContent();
        public void UnloadContent();
        public void Update(GameTime gameTime);
        public void Draw(GameTime gameTime);
    }
}
