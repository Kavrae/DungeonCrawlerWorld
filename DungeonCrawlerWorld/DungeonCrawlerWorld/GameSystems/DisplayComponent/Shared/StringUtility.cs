using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Text;

namespace DungeonCrawlerWorld.GameComponents.DisplayComponent
{
    public static class StringUtility
    { //TODO Computationally expensive. Singleton pattern - do it once (based on screen size) then store it somewhere for retrieval.
        //TODO replace all string text with RenderedText structs, containing the original text and the rendered text with formatting marks (like linebreaks).
        //Use renderedText if it's not null, coalescing to original text.
        //TODO need one version for each type of display. THis one is for SelectionDisplay
        public static string FormatText(SpriteFont font, string text, Point textboxSize, bool wordWrap, bool truncate)
        {
            var maximumSize = textboxSize.X * textboxSize.Y;

            if (text == null)
            {
                text = string.Empty;
            }

            if (wordWrap)
            {
                text = WordWrap(font, text, textboxSize, truncate);
            }
            else if (truncate)
            {
                text = Truncate(font, text, textboxSize.X);
            }

            return text;
        }

        public static string Truncate(SpriteFont font, string text, int maximumWidth)
        {
            var textWidth = font.MeasureString(text).X;
            if (textWidth > maximumWidth)
            {
                var percentageDifference = maximumWidth / textWidth;
                var substringEstimate = (int)Math.Round(percentageDifference * text.Length, MidpointRounding.AwayFromZero);
                text = text[..substringEstimate];
                textWidth = font.MeasureString(text).X;
            }
            while (textWidth > maximumWidth)
            {
                text = text[..^1];
                textWidth = font.MeasureString(text).X;
            }

            return text;
        }

        public static string WordWrap(SpriteFont font, string text, Point maximumSize, bool truncate)
        {
            var spaceSize = font.MeasureString(" ");
            var maximumLines = (int)(maximumSize.Y / spaceSize.Y);
            return WordWrap(font, text, maximumSize.X, spaceSize.X, 0, truncate, maximumLines);
        }

        public static string WordWrap(SpriteFont font, string text, float maximumLineWidth, float spaceWidth, int currentLineCount, bool truncate, int maximumLines)
        {
            var words = text.Split(' ');
            var stringBuilder = new StringBuilder();
            var currentLineWidth = 0f;

            foreach (var word in words)
            {
                var wordSize = font.MeasureString(word).X;

                if (currentLineWidth + wordSize < maximumLineWidth)
                {
                    stringBuilder.Append(word + " ");
                    currentLineWidth += wordSize + spaceWidth;
                }
                else if (!truncate || currentLineCount < maximumLines)
                {
                    if (wordSize > maximumLineWidth)
                    {
                        if (stringBuilder.Length > 0)
                        {
                            stringBuilder.Append(Environment.NewLine);
                            currentLineCount++;
                        }
                        stringBuilder.Append(WordWrap(font, word.Insert(word.Length / 2, " "), maximumLineWidth, spaceWidth, currentLineCount, truncate, maximumLines));
                    }
                    else
                    {
                        stringBuilder.Append(Environment.NewLine + word + " ");
                        currentLineWidth = wordSize + spaceWidth;
                        currentLineCount++;
                    }
                }
            }

            return stringBuilder.ToString();
        }
    }
}
