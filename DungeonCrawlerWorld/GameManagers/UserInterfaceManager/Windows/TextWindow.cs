using DungeonCrawlerWorld.Services;
using DungeonCrawlerWorld.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class TextWindow : Window
    {
        public string OriginalText { get; set; }
        public DisplayText DisplayText { get; set; }
        public SpriteFontBase ContentFont { get; set; }
        public Color TextColor { get; set; }
        private readonly int LinePadding = 3;

        public TextWindow(Window parentWindow, TextWindowOptions windowOptions) : base(parentWindow, windowOptions)
        {
            ContentFont = FontService.GetFont(8);
            OriginalText = windowOptions.Text;
            TextColor = windowOptions.TextColor ?? Color.Black;

            _canContainChildWindows = false;
        }

        public override void Initialize()
        {
            base.Initialize();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);
        }

        public override void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
            if (!string.IsNullOrWhiteSpace(DisplayText.FormattedText))
            {
                spriteBatch.DrawString(
                    ContentFont,
                    DisplayText.FormattedText,
                    new Vector2(
                        LinePadding,
                        LinePadding),
                    TextColor);
            }
        }

        public override void RecalculateStaticWindowSize()
        {
            base.RecalculateStaticWindowSize();

            ReformatDisplayText();
        }

        public override void RecalculateFillWindowSize()
        {
            base.RecalculateFillWindowSize();

            ReformatDisplayText();
        }

        public override void RecalculateGrowWindowSize()
        {
            _contentSize.X = _windowMaximumSize.X - _windowRelativePosition.X;

            if (_showBorder)
            {
                _contentSize.X -= _borderSize.X * 2;
            }

            ReformatDisplayText();

            _contentSize.Y = ContentFont.LineHeight * DisplayText.LineCount + (LinePadding * (DisplayText.LineCount + 1));

            _windowCurrentSize = _contentSize;
            if (_showTitle)
            {
                //Resize horizontally to fit the new content size, but keep the vertical size
                _titleSize = new Vector2(
                    _contentSize.X,
                    _originalTitleSize.Y - (_showBorder ? _borderSize.Y : 0));
                _windowCurrentSize.Y += _titleSize.Y;
            }
            if (_showBorder)
            {
                _windowCurrentSize += Vector2.Multiply(_borderSize, 2);
            }
        }

        public void ReformatDisplayText()
        {
            DisplayText = StringUtility.FormatText(new FormatTextCriteria(ContentFont, _contentSize.X - (ContentPadding.X * 2), OriginalText, FormatTextMode.Wordwrap));
        }

        public void UpdateText(string newText)
        {
            OriginalText = newText;

            switch (_windowDisplayMode)
            {
                case WindowDisplayMode.Static:
                    RecalculateStaticWindowSize();
                    break;
                case WindowDisplayMode.Fill:
                    RecalculateFillWindowSize();
                    break;
                case WindowDisplayMode.Grow:
                    RecalculateGrowWindowSize();
                    break;
            }
        }

        protected override void OnContentClickAction(Point mousePosition)
        {
            //TODO copy text to clipboard
        }
    }
}