using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.GameComponents.DisplayComponent
{
    public interface IDisplayComponent
    {
        public FontService FontService { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }
        public Rectangle DisplayRectangle { get; set; }

        public void Initialize();
        public void LoadContent();
        public void Update(GameTime gameTime);
        public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle);
    }
}
