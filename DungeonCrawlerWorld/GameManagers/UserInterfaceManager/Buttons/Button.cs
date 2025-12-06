using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class Button
    {
        public Guid ButtonId { get; }

        public Window ParentWindow { get; set; }

        public Vector2 RelativePosition { get; set; }
        public Vector2 AbsolutePosition { get; set; }

        public Vector2 Size { get; set; }
        public Vector2 DefaultSize = new(50, 50);

        public Rectangle ButtonRectangle;
        public Rectangle ContentRectangle;

        public Color ButtonColor { get; set; }

        public bool ShowBorder { get; set; }

        public string Text { get; set; }
        public Vector2 TextOffset { get; set; }

        protected SpriteFontBase Font;

        //TODO common button templates, same way I have window templates
        public Button(Window parentWindow, ButtonOptions buttonOptions)
        {
            ButtonId = Guid.NewGuid();
            ParentWindow = parentWindow;

            Text = buttonOptions.Text ?? string.Empty;
            TextOffset = buttonOptions.TextOffset ?? new Vector2(2, -4);
            Font = buttonOptions.Font;

            RelativePosition = buttonOptions.RelativePosition ?? Vector2.Zero;
            Size = buttonOptions.Size ?? DefaultSize;
            ShowBorder = buttonOptions.ShowBorder ?? false;
            ButtonColor = buttonOptions.Color ?? Color.White;
        }

        public virtual void Initialize()
        {
            CalculateButtonPositionAndRectangle();
        }

        public virtual void Update(GameTime gameTime)
        {
        }

        //TODO extra border to make 3d + slightly darker color for hover effect
        public void Draw(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            if (ShowBorder)
            {
                spriteBatch.Draw(unitRectangle, ButtonRectangle, Color.Black);
            }

            spriteBatch.Draw(unitRectangle, ContentRectangle, ButtonColor);

            if (!string.IsNullOrWhiteSpace(Text))
            {
                spriteBatch.DrawString(Font, Text, AbsolutePosition + TextOffset, Color.Black);
            }
        }

        public void ChangeRelativePosition(Vector2 newPosition)
        {
            RelativePosition = newPosition;
            CalculateButtonPositionAndRectangle();
        }

        public void CalculateButtonPositionAndRectangle()
        {
            AbsolutePosition = RelativePosition + ParentWindow.WindowAbsolutePosition;
            ButtonRectangle = new Rectangle((int)AbsolutePosition.X, (int)AbsolutePosition.Y, (int)Size.X, (int)Size.Y);
            if (ShowBorder)
            {
                //Decrease bottom and right by 1 to show those borders
                ContentRectangle = new Rectangle(ButtonRectangle.X, ButtonRectangle.Y, ButtonRectangle.Width - 1, ButtonRectangle.Height - 1);
            }
            else
            {
                ContentRectangle = ButtonRectangle;
            }
        }

        public void HandleClick(Point mousePosition)
        {
            OnClickAction(mousePosition);
        }

        protected virtual void OnClickAction(Point mousePosition)
        {
            var test = "TODO";
        }
    }
}
