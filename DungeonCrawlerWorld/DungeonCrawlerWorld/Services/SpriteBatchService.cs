using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.Services
{
    public interface ISpriteManager
    {
        SpriteBatch StartSpriteBatch();
        void EndSpriteBatch();
    }

    public class SpriteBatchService : ISpriteManager
    {
        private GraphicsDevice _graphicsDevice;
        private SpriteBatch _spriteBatch;

        public SpriteBatchService(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
        }

        public SpriteBatch StartSpriteBatch()
        {
            _spriteBatch = new SpriteBatch(_graphicsDevice);
            _spriteBatch.Begin();
            return _spriteBatch;
        }

        public void EndSpriteBatch()
        {
            _spriteBatch.End();
            _spriteBatch.Dispose();
        }
    }
}
