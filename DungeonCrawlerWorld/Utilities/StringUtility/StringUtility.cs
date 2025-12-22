using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            var displayText = new DisplayText(criteria.OriginalText ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(criteria.OriginalText))
            {
                criteria.TextLinesToFormat = ParseNewlineCharacters(criteria.OriginalText);

                //Wordwrap overrides truncate
                if (criteria.WordWrap)
                {
                    WordWrap(displayText, criteria);
                }
                else if (criteria.Truncate)
                {
                    Truncate(displayText, criteria);
                }
            }

            return displayText;
        }

        /// <summary>
        /// Splits the original text into separate lines based on newline characters, allowing callers to specify manual line breaks.
        /// </summary>
        /// <param name="OriginalText"></param>
        /// <returns>A list of strings, each representing a separate line of text, to be stored in the DisplayText's FormattedTextLines property.</returns>
        public static List<string> ParseNewlineCharacters(string OriginalText)
        {
            return OriginalText
                .Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        /// <summary>
        /// Truncates each text line to fit within the specified maximum pixel width.
        /// Substring percentage calculations rounded to 0 are used to avoid characters running past the maximum width.
        /// </summary>
        private static void Truncate(DisplayText displayText, FormatTextCriteria criteria)
        {
            foreach (var textLine in criteria.TextLinesToFormat)
            {
                var textWidth = criteria.Font.MeasureString(textLine).X;

                if (textWidth > criteria.MaximumPixelWidth)
                {
                    var percentageDifference = criteria.MaximumPixelWidth / textWidth;
                    var substringLength = (int)Math.Round(percentageDifference * textLine.Length, MidpointRounding.ToZero);
                    displayText.FormattedTextLines.Add(textLine[..substringLength]);

                    displayText.IsTruncated = true;
                }
                else
                {
                    displayText.FormattedTextLines.Add(textLine);
                }
            }
        }

        /// <summary>
        /// Determines the appropriate word wrapping method based on the maximum pixel width and applies it to the display text.
        /// Small text boxes do not have enough room for hyphenation and use a simpler word wrap method.
        /// </summary>
        private static void WordWrap(DisplayText displayText, FormatTextCriteria criteria)
        {
            var minimumWordSizeToHyphenate = minimumCharacterCountToLineBreak * criteria.CharacterWidth;

            if (criteria.MaximumPixelWidth >= minimumWordSizeToHyphenate)
            {
                WordWrapWithHyphenation(displayText, criteria, minimumWordSizeToHyphenate);
            }
            else
            {
                SimpleWordWrap(displayText, criteria);
            }
        }

        /// <summary>
        /// A simple more space efficient word wrap based on character pixel widths, similar to the Truncate method.
        /// </summary>
        public static void SimpleWordWrap(DisplayText displayText, FormatTextCriteria criteria)
        {
            foreach (var textLine in criteria.TextLinesToFormat)
            {
                var textSize = criteria.Font.MeasureString(textLine).X;

                if (textSize <= criteria.MaximumPixelWidth)
                {
                    displayText.FormattedTextLines.Add(textLine);
                }
                else
                {
                    var percentageOfTextThatFits = criteria.MaximumPixelWidth / textSize;
                    var charactersPerLine = (int)Math.Round(percentageOfTextThatFits * textLine.Length, MidpointRounding.ToZero);
                    for (var i = 0; i < textLine.Length; i += charactersPerLine)
                    {
                        var lastCharacter = Math.Min(charactersPerLine, textLine.Length - i - 1);
                        displayText.FormattedTextLines.Add(textLine.Substring(i, lastCharacter));
                    }
                }
            }
        }

        /// <summary>
        /// Applies word wrap to each text line in TextLinesToFormat, which allows each line to separately wrap without affecting the line order.
        /// Words are first wrapped based on word breaks (spaces). If a word exceeds the maximum pixel width, it is hyphenated and broken across multiple lines.
        /// Hyphenation positioning is basded on the minimum character counts before and after the line break.
        /// </summary>
        private static void WordWrapWithHyphenation(DisplayText displayText, FormatTextCriteria criteria, float minimumWordSizeToLineBreak)
        {
            foreach (var textLine in criteria.TextLinesToFormat)
            {
                var textSize = criteria.Font.MeasureString(textLine).X;

                if (textSize <= criteria.MaximumPixelWidth)
                {
                    displayText.FormattedTextLines.Add(textLine);
                }
                else
                {
                    var words = textLine.Split(' ');
                    var currentLineStringBuilder = new StringBuilder();
                    var remainingLineWidth = criteria.MaximumPixelWidth;

                    foreach (var word in words)
                    {
                        if (remainingLineWidth != criteria.MaximumPixelWidth)
                        {
                            currentLineStringBuilder.Append(' ');
                            remainingLineWidth -= criteria.CharacterWidth;
                        }

                        var remainingWord = word;
                        do
                        {
                            var wordSize = criteria.Font.MeasureString(remainingWord).X;

                            //Word fits on the current line
                            //Add and move on to the next word in the line
                            if (remainingLineWidth - wordSize > 0)
                            {
                                currentLineStringBuilder.Append(remainingWord);
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
                                    var substringLength = (int)Math.Round(percentageOfWordThatFits * remainingWord.Length, MidpointRounding.ToZero);
                                    substringLength = Math.Max(substringLength, minimumCharactersBeforeLineBreak);
                                    substringLength = Math.Min(substringLength, remainingWord.Length - minimumCharactersAfterLineBreak);
                                    var hyphenatedSubstring = string.Concat(remainingWord[..substringLength], "-");
                                    currentLineStringBuilder.Append(hyphenatedSubstring);
                                    remainingLineWidth -= criteria.Font.MeasureString(hyphenatedSubstring).X;
                                    remainingWord = remainingWord[substringLength..];
                                }
                                //Not enough space left to linebreak or word is too small. Add the current line and start a new one.
                                else
                                {
                                    displayText.FormattedTextLines.Add(currentLineStringBuilder.ToString());
                                    remainingLineWidth = criteria.MaximumPixelWidth;
                                    currentLineStringBuilder.Clear();
                                }
                            }
                        }
                        while (remainingWord.Length > 0);
                    }

                    //Add last line if there's any text remaining
                    if (currentLineStringBuilder.Length > 0)
                    {
                        var lastLine = currentLineStringBuilder.ToString();
                        if (!string.IsNullOrWhiteSpace(lastLine))
                        {
                            displayText.FormattedTextLines.Add(lastLine);
                        }
                    }
                }
            }
        }
    }
}
