using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.GameComponents.DisplayComponent
{
    public class DebugDisplay : IDisplayComponent
    {
        public FontService FontService { get; set; }
        private DataAccess _dataAccess;

        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }
        public Rectangle DisplayRectangle { get; set; }

        private int _drawCount;
        private int _updateCount;
        private double _drawsPerSecond;
        private double _updatesPerSecond;
        private GameVariables _gameVariables;

        public DebugDisplay(DataAccess dataAccess, Vector2 position, Vector2 size)
        {
            FontService = GameServices.GetService<FontService>();
            _dataAccess = dataAccess;

            Size = size;
            Position = position;
            DisplayRectangle = new Rectangle(position.ToPoint(), Size.ToPoint());
        }

        public void Initialize() { }

        public void LoadContent() { }

        public void Update(GameTime gameTime)
        {
            _updateCount++;
            _updatesPerSecond = _updateCount / gameTime.TotalGameTime.TotalSeconds;

            _gameVariables = _dataAccess.RetrieveGameVariables();
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            _drawCount++;
            _drawsPerSecond = _drawCount / gameTime.TotalGameTime.TotalSeconds;

            var font = FontService.GetFont("defaultFont");
            spriteBatch.DrawString(font, $"{string.Format("{0:N1}",_updatesPerSecond)} ups", Position, gameTime.IsRunningSlowly ? Color.Red : Color.Black);

            spriteBatch.DrawString(font, $"{string.Format("{0:N1}", _drawsPerSecond)} fps", new Vector2(Position.X + 60, Position.Y), gameTime.IsRunningSlowly ? Color.Red : Color.Black);

            if(_gameVariables.IsPaused)
            {
                spriteBatch.DrawString(font, "Paused", new Vector2(Position.X + 120, Position.Y), Color.Red);
            }
        }
    }
}
