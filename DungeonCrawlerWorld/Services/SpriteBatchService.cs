using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.Services
{
    public interface ISpriteManager
    {
        SpriteBatch StartSpriteBatch();
        void EndSpriteBatch();
    }

    /// <summary>
    /// Provides a service to manage the lifecycle of SpriteBatch instances.
    /// Safer than having each manager or system create their own SpriteBatch instances and less boilerplate than passing them around.
    /// </summary>
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
