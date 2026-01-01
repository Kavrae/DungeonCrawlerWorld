using System;
using System.Text;
using System.Text.RegularExpressions;

namespace DungeonCrawlerWorld.Utilities
{
    /// <summary>
    /// Provides utility methods for formatting and manipulating strings to be displayed in the User Interface.
    /// Performance of this class is critical as it is called frequently during UI rendering.
    /// </summary>
    public static class StringUtility
    {
        /// <summary>
        /// Minimum number of characters required to consider a line break. 
        /// Words shorter than this value will be placed onto the next line instead of breaking.
        /// </summary>
        private const int minimumCharacterCountToLineBreak = 6;

        /// <summary>
        /// The minimum number of characters that must appear before a line break when hyphenating.
        /// If the remaining characters in a line is less than this value, the entire word is moved to the next line.
        /// </summary>
        private const int minimumCharactersBeforeLineBreak = 3;

        /// <summary>
        /// The minimum number of characters that must appear after a line break when hyphenating.
        /// This value is used to determine how many characters to place on the next line after hyphenating a word.
        /// </summary>
        private const int minimumCharactersAfterLineBreak = minimumCharacterCountToLineBreak - minimumCharactersBeforeLineBreak;

        /// <summary>
        /// Build a UI bar of a specified barSize, filled to a percentage based on the current value vs maximum value.
        /// </summary>
        public static string BuildPercentageBar(int currentValue, int maximumValue, int barSize)
        {
            int fillCount;
            if (currentValue == 0)
            {
                fillCount = 0;
            }
            else
            {
                fillCount = (int)((float)barSize * currentValue / maximumValue);
            }

            var spanSize = barSize + 7;
            // total length: "HP : [" (6) + 20 chars + "]" (1) = 27
            return string.Create(spanSize, fillCount, (span, fillCount) =>
            {
                // prefix
                span[0] = 'H'; span[1] = 'P'; span[2] = ' '; span[3] = ':'; span[4] = ' '; span[5] = '[';

                // bars
                for (int i = 0; i < maximumValue; i++)
                {
                    span[6 + i] = i < fillCount ? '=' : '_';
                }

                // suffix
                span[spanSize - 1] = ']';
            });
        }

        /// <summary>
        /// Formats the given text based on the specified criteria, including options for truncation and word wrapping.
        /// </summary>
        /// <returns>DisplayText to be consumed by UserInterface draw calls.</returns>
        public static DisplayText FormatText(FormatTextCriteria criteria)
        {
            if (string.IsNullOrWhiteSpace(criteria.OriginalText))
            {
                return new DisplayText(string.Empty, string.Empty, 0);
            }
            else if (criteria.TextWidth <= criteria.MaximumPixelWidth)
            {
                return new DisplayText(criteria.OriginalText, criteria.OriginalText, CountNewlineCharacters(criteria.OriginalText));
            }
            else if (criteria.FormatTextMode == FormatTextMode.Wordwrap)
            {
                return WordWrap(criteria);
            }
            else
            {
                return Truncate(criteria);
            }
        }

        /// <summary>
        /// Truncates each text line to fit within the specified maximum pixel width.
        /// Substring percentage calculations rounded to 0 are used to avoid characters running past the maximum width.
        /// </summary>
        private static DisplayText Truncate(FormatTextCriteria criteria)
        {
            var percentageDifference = criteria.MaximumPixelWidth / criteria.TextWidth;
            var substringLength = (int)Math.Round(percentageDifference * criteria.OriginalText.Length, MidpointRounding.ToZero);
            return new DisplayText(criteria.OriginalText, criteria.OriginalText[..substringLength], 1);
        }

        /// <summary>
        /// Determines the appropriate word wrapping method based on the maximum pixel width and applies it to the display text.
        /// Small text boxes do not have enough room for hyphenation and use a simpler word wrap method.
        /// </summary>
        private static DisplayText WordWrap(FormatTextCriteria criteria)
        {
            var minimumWordSizeToHyphenate = minimumCharacterCountToLineBreak * criteria.CharacterWidth;

            if (criteria.MaximumPixelWidth >= minimumWordSizeToHyphenate)
            {
                return WordWrapWithHyphenation(criteria, minimumWordSizeToHyphenate);
            }
            else
            {
                return SimpleWordWrap(criteria);
            }
        }

        /// <summary>
        /// A simple more space efficient word wrap based on character pixel widths.
        /// </summary>
        public static DisplayText SimpleWordWrap(FormatTextCriteria criteria)
        {
            var lineLength = (int)(criteria.MaximumPixelWidth / criteria.CharacterWidth);
            var formattedText = Regex.Replace(criteria.OriginalText, "(.{" + lineLength + "})", "$1" + Environment.NewLine);
            return new DisplayText(criteria.OriginalText, formattedText, CountNewlineCharacters(formattedText));
        }

        /// <summary>
        /// Applies word wrap to each text line in TextLinesToFormat, which allows each line to separately wrap without affecting the line order.
        /// Words are first wrapped based on word breaks (spaces). If a word exceeds the maximum pixel width, it is hyphenated and broken across multiple lines.
        /// Hyphenation positioning is basded on the minimum character counts before and after the line break.
        /// </summary>
        private static DisplayText WordWrapWithHyphenation(FormatTextCriteria criteria, float minimumWordSizeToLineBreak)
        {
            var words = criteria.OriginalText.Split(' ');
            var stringBuilder = new StringBuilder();
            var remainingLineWidth = criteria.MaximumPixelWidth;

            foreach (var word in words)
            {
                if (remainingLineWidth != criteria.MaximumPixelWidth)
                {
                    stringBuilder.Append(' ');
                    remainingLineWidth -= criteria.CharacterWidth;
                }

                var remainingWord = word;
                do
                {
                    var wordSize = criteria.Font.MeasureString(remainingWord).X;

                    //Word fits on the current line
                    //Add and move on to the next word in the line
                    if (remainingLineWidth > wordSize)
                    {
                        stringBuilder.Append(remainingWord);
                        remainingLineWidth -= wordSize;
                        break;
                    }
                    //Word doesn't fit. Loop breaking the word up until the word is complete.
                    else
                    {
                        var remainingHyphenatedLineWidth = remainingLineWidth - criteria.CharacterWidth;

                        //Enough space left to linebreak with '-' and string is long enought to break
                        if (remainingHyphenatedLineWidth >= minimumWordSizeToLineBreak && remainingWord.Length >= minimumCharacterCountToLineBreak)
                        {
                            var percentageOfWordThatFits = remainingHyphenatedLineWidth / wordSize;
                            var substringLength = MathUtility.ClampInt(
                                (int)Math.Round(percentageOfWordThatFits * remainingWord.Length, MidpointRounding.ToZero),
                                minimumCharactersBeforeLineBreak,
                                remainingWord.Length - minimumCharactersAfterLineBreak
                            );
                            var hyphenatedSubstring = string.Concat(remainingWord[..substringLength], "-");
                            stringBuilder.Append(hyphenatedSubstring);
                            remainingLineWidth -= criteria.Font.MeasureString(hyphenatedSubstring).X;
                            remainingWord = remainingWord[substringLength..];
                        }
                        //Not enough space left to linebreak or word is too small. Add the current line and start a new one.
                        else
                        {
                            stringBuilder.Append(Environment.NewLine);
                            remainingLineWidth = criteria.MaximumPixelWidth;
                        }
                    }
                }
                while (remainingWord.Length > 0);
            }
            var formattedText = stringBuilder.ToString();
            return new DisplayText(criteria.OriginalText, formattedText, CountNewlineCharacters(formattedText));
        }

        /// <summary>
        /// Count the number newline characters in the string. This is used over counting the number of lines created during formatting as 
        /// text can be passed to StringUtility with pre-existing newline characters for forced line breaks.
        /// </summary>
        private static int CountNewlineCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            int count = 1;
            foreach (char character in text)
            {
                if (character == '\n')
                {
                    count++;
                }
            }
            return count;
        }
    }
}
