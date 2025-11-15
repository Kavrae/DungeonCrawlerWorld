using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using DungeonCrawlerWorld.Utilities;
using DungeonCrawlerWorld.Services;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class TextWindow : Window
    {
        public string OriginalText { get; set; }
        public DisplayText FormattedText { get; set; }
        public SpriteFont ContentFont { get; set; }
        public Color TextColor { get; set; }
        private readonly int LinePadding = 3;

        public TextWindow(Window parentWindow, TextWindowOptions windowOptions) : base(parentWindow, windowOptions)
        {
            ContentFont = FontService.GetFont("defaultFont");
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
            if (FormattedText != null && FormattedText.FormattedTextLines != null && FormattedText.FormattedTextLines.Count > 0)
            {
                for (int lineNumber = 0; lineNumber < FormattedText.FormattedTextLines.Count; lineNumber++)
                {
                    var line = FormattedText.FormattedTextLines[lineNumber];
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        spriteBatch.DrawString(
                            ContentFont,
                            line,
                            new Vector2(
                                LinePadding,
                                (ContentFont.LineSpacing * lineNumber) + (LinePadding * (lineNumber + 1))),
                            TextColor);
                    }
                }
            }
        }

        public override void RecalculateStaticWindowSize()
        {
            base.RecalculateStaticWindowSize();

            FormattedText = StringUtility.FormatText(new FormatTextCriteria (OriginalText)
            {
                Font = ContentFont,
                MaximumPixelWidth = _contentSize.X - (ContentPadding.X * 2),
                WordWrap = true
            });
        }

        public override void RecalculateFillWindowSize()
        {
            base.RecalculateFillWindowSize();

            FormattedText = StringUtility.FormatText(new FormatTextCriteria (OriginalText)
            {
                Font = ContentFont,
                MaximumPixelWidth = _contentSize.X - (ContentPadding.X * 2),
                WordWrap = true
            });
        }

        public override void RecalculateGrowWindowSize()
        {
            _contentSize.X = _windowMaximumSize.X - _windowRelativePosition.X;

            if (_showBorder)
            {
                _contentSize.X -= _borderSize.X * 2;
            }

            FormattedText = StringUtility.FormatText(new FormatTextCriteria(OriginalText)
            {
                Font = ContentFont,
                MaximumPixelWidth = _contentSize.X - (ContentPadding.X * 2),
                WordWrap = true
            });

            _contentSize.Y = ContentFont.LineSpacing * FormattedText.FormattedTextLines.Count + (LinePadding * (FormattedText.FormattedTextLines.Count + 1));

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

        protected override void OnContentClickAction(Vector2 mousePosition)
        {
            //TODO copy text to clipboard
        }
    }
}