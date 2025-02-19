using System;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class DebugDisplay : UserInterfaceComponent
    {
        private World world;

        private readonly long ticksBetweenUpdates = 10000000; //1 seconds
        private long lastDrawTicks;
        private long lastUpdateTicks;
        private long drawsSinceLastDisplayUpdate;
        private long updatesSinceLastDisplayUpdate;
        private double drawsPerSecond;
        private double updatesPerSecond;
        private GameVariables gameVariables;

        public DebugDisplay(World dataAccess, Point position, Point size) : base(position, size)
        {
            world = dataAccess;
        }

        public override void Initialize()
        {
            FontService = GameServices.GetService<FontService>();
            lastDrawTicks = DateTime.Now.Ticks;
            lastUpdateTicks = DateTime.Now.Ticks;
        }

        public override void LoadContent() { }

        public override void Update(GameTime gameTime)
        {
            updatesSinceLastDisplayUpdate += 1;
            gameVariables = world.RetrieveGameVariables();

            var currentTicks = DateTime.Now.Ticks;
            var ticksSinceLastUpdate = currentTicks - lastUpdateTicks;
            if( ticksSinceLastUpdate >= ticksBetweenUpdates)
            {
                updatesPerSecond = updatesSinceLastDisplayUpdate;
                lastUpdateTicks = currentTicks;

                updatesSinceLastDisplayUpdate = 0;
            }
        }

        public override void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            drawsSinceLastDisplayUpdate += 1;

            var currentTicks = DateTime.Now.Ticks;
            var ticksSinceLastDraw = currentTicks - lastDrawTicks;
            if (ticksSinceLastDraw >= ticksBetweenUpdates)
            {
                drawsPerSecond = drawsSinceLastDisplayUpdate;
                lastDrawTicks = currentTicks;

                drawsSinceLastDisplayUpdate = 0;
            }

            var font = FontService.GetFont("defaultFont");
            spriteBatch.DrawString(font, $"{string.Format("{0:N1}",updatesPerSecond)} ups", Position.ToVector2(), gameTime.IsRunningSlowly ? Color.Red : Color.Black);

            spriteBatch.DrawString(font, $"{string.Format("{0:N1}", drawsPerSecond)} fps", new Vector2(Position.X + 60, Position.Y), gameTime.IsRunningSlowly ? Color.Red : Color.Black);

            if(gameVariables.IsPaused)
            {
                spriteBatch.DrawString(font, "Paused", new Vector2(Position.X + 120, Position.Y), Color.Red);
            }

            spriteBatch.DrawString(font, $"Entities : {string.Format("{0:N0}", ComponentRepo.TransformComponents.Count)}", new Vector2(Position.X + 180, Position.Y), Color.Black);
            spriteBatch.DrawString(font, $"Moving Entities : {string.Format("{0:N0}", ComponentRepo.MovementComponents.Count)}", new Vector2(Position.X + 300, Position.Y), Color.Black);
        }
    }
}
