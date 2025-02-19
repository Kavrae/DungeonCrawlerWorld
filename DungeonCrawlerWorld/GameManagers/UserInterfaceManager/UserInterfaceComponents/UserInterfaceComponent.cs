using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public abstract class UserInterfaceComponent
    {
        public FontService FontService { get; set; }
        public Point Position { get; set; }
        public Point Size { get; set; }
        public Rectangle DisplayRectangle { get; set; }

        public UserInterfaceComponent(Point position, Point size)
        {
            Position = position;
            Size = size;
            DisplayRectangle = new Rectangle(position.X, position.Y, Size.X, Size.Y);
        }

        public abstract void Initialize();
        public abstract void LoadContent();
        public abstract void Update(GameTime gameTime);
        public abstract void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle);
        public bool IsInDisplayRectangle(Point point)
        {
            return DisplayRectangle.Contains(point);
        }

    }
}
