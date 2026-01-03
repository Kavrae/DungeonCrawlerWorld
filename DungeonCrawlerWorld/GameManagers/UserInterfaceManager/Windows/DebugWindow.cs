using DungeonCrawlerWorld.Components;
using DungeonCrawlerWorld.Services;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class DebugWindow : Window
    {
        private readonly long ticksBetweenUpdates = 10000000; //1 seconds
        private long lastDrawTicks;
        private long lastUpdateTicks;
        private long drawsSinceLastDisplayUpdate;
        private long updatesSinceLastDisplayUpdate;

        private double drawsPerSecond;
        private string drawsPerSecondString = string.Empty;

        private double updatesPerSecond;
        private string updatesPerSecondString = string.Empty;

        private double entityCount;
        private string entityCountString = string.Empty;

        private double movingEntityCount;
        private string movingEntityCountString = string.Empty;

        private SpriteFontBase font;

        public DebugWindow() : base()
        {
        }

        public void BuildWindow(Window parentWindow, TextWindowOptions windowOptions)
        {
            base.BuildWindow(parentWindow, windowOptions);
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
                var newUpdatesPerSecond = updatesSinceLastDisplayUpdate;
                lastUpdateTicks = currentTicks;
                updatesSinceLastDisplayUpdate = 0;

                if (newUpdatesPerSecond != updatesPerSecond)
                {
                    updatesPerSecond = newUpdatesPerSecond;
                    updatesPerSecondString = $"{string.Format("{0:N1}", updatesPerSecond)} ups";
                }
            }

            var newEntityCount = ComponentRepo.CurrentMaxEntityId;
            if (newEntityCount != entityCount)
            {
                entityCount = newEntityCount;
                entityCountString = $"Entities : {string.Format("{0:N0}", entityCount)}";
            }

            var newMovingEntityCount = ComponentRepo.MovementComponents.Count;
            if (newMovingEntityCount != movingEntityCount)
            {
                movingEntityCount = newMovingEntityCount;
                movingEntityCountString = $"Moving Entities : {string.Format("{0:N0}", movingEntityCount)}";
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
                var newDrawsPerSecond = drawsSinceLastDisplayUpdate;
                lastDrawTicks = currentTicks;
                drawsSinceLastDisplayUpdate = 0;

                if (newDrawsPerSecond != drawsPerSecond)
                {
                    drawsPerSecond = newDrawsPerSecond;
                    drawsPerSecondString = $"{string.Format("{0:N1}", drawsPerSecond)} fps";
                }
            }

            spriteBatch.DrawString(font, updatesPerSecondString, _contentAbsolutePosition, gameTime.IsRunningSlowly ? Color.Red : Color.Black);

            spriteBatch.DrawString(font, drawsPerSecondString, new Vector2(_contentAbsolutePosition.X + 60, _contentAbsolutePosition.Y), gameTime.IsRunningSlowly ? Color.Red : Color.Black);

            if (_gameVariables.IsPaused)
            {
                spriteBatch.DrawString(font, "Paused", new Vector2(_contentAbsolutePosition.X + 120, _contentAbsolutePosition.Y), Color.Red);
            }

            spriteBatch.DrawString(font, entityCountString, new Vector2(_contentAbsolutePosition.X + 180, _contentAbsolutePosition.Y), Color.Black);
            spriteBatch.DrawString(font, movingEntityCountString, new Vector2(_contentAbsolutePosition.X + 300, _contentAbsolutePosition.Y), Color.Black);
        }
    }
}
