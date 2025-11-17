using System;

using FontStashSharp;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Data;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class DebugWindow : Window
    {
        private World world;

        private readonly long ticksBetweenUpdates = 10000000; //1 seconds
        private long lastDrawTicks;
        private long lastUpdateTicks;
        private long drawsSinceLastDisplayUpdate;
        private long updatesSinceLastDisplayUpdate;
        private double drawsPerSecond;
        private double updatesPerSecond;

        private SpriteFontBase font;

        public DebugWindow(World dataAccess, WindowOptions windowOptions) : base(null, windowOptions)
        {
            world = dataAccess;
        }

        public override void Initialize()
        {
            base.Initialize();

            lastDrawTicks = DateTime.Now.Ticks;
            lastUpdateTicks = DateTime.Now.Ticks;

            font = FontService.GetFont(8);
        }

        public override void LoadContent()
        {
            base.LoadContent();
        }

        public override void Update(GameTime gameTime)
        {
            updatesSinceLastDisplayUpdate += 1;

            var currentTicks = DateTime.Now.Ticks;
            var ticksSinceLastUpdate = currentTicks - lastUpdateTicks;
            if (ticksSinceLastUpdate >= ticksBetweenUpdates)
            {
                updatesPerSecond = updatesSinceLastDisplayUpdate;
                lastUpdateTicks = currentTicks;

                updatesSinceLastDisplayUpdate = 0;
            }

            base.Update(gameTime);
        }

        public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
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

            spriteBatch.DrawString(font, $"{string.Format("{0:N1}", updatesPerSecond)} ups", _contentAbsolutePosition, gameTime.IsRunningSlowly ? Color.Red : Color.Black);

            spriteBatch.DrawString(font, $"{string.Format("{0:N1}", drawsPerSecond)} fps", new Vector2(_contentAbsolutePosition.X + 60, _contentAbsolutePosition.Y), gameTime.IsRunningSlowly ? Color.Red : Color.Black);

            if (_gameVariables.IsPaused)
            {
                spriteBatch.DrawString(font, "Paused", new Vector2(_contentAbsolutePosition.X + 120, _contentAbsolutePosition.Y), Color.Red);
            }

            spriteBatch.DrawString(font, $"Entities : {string.Format("{0:N0}", ComponentRepo.CurrentMaxEntityId)}", new Vector2(_contentAbsolutePosition.X + 180, _contentAbsolutePosition.Y), Color.Black);
            spriteBatch.DrawString(font, $"Moving Entities : {string.Format("{0:N0}", ComponentRepo.MovementComponents.Count)}", new Vector2(_contentAbsolutePosition.X + 300, _contentAbsolutePosition.Y), Color.Black);
        }
    }
}
