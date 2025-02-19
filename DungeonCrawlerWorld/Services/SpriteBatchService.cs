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
        private SpriteBatch spriteBatch;

        public SpriteBatchService(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
        }

        public SpriteBatch StartSpriteBatch()
        {
            spriteBatch = new SpriteBatch(_graphicsDevice);
            spriteBatch.Begin();
            return spriteBatch;
        }

        public SpriteBatch GetSpriteBatch()
        {
            return spriteBatch;
        }

        public void EndSpriteBatch()
        {
            spriteBatch.End();
            spriteBatch.Dispose();
        }
    }
}
