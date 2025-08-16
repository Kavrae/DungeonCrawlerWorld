using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DungeonCrawlerWorld.Utilities
{
    public static class StringUtility
    {
        private static readonly int minimumCharacterCountToLineBreak = 6;
        private static readonly int minimumCharactersBeforeLineBreak = 3;
        private static readonly int minimumCharactersAfterLineBreak = minimumCharacterCountToLineBreak - minimumCharactersBeforeLineBreak;

        public static DisplayText FormatText(FormatTextCriteria criteria)
        {
            var displayText = new DisplayText
            {
                OriginalText = criteria.OriginalText,
                FormattedTextLines = new List<string>()
            };

            criteria.TextLinesToFormat = ParseNewlineCharacters(criteria.OriginalText);

            if (!string.IsNullOrWhiteSpace(criteria.OriginalText))
            {
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

        public static List<string> ParseNewlineCharacters(string OriginalText)
        {
            return OriginalText
                .Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

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

        private static void WordWrap(DisplayText displayText, FormatTextCriteria criteria)
        {
            var minimumWordSizeToHyphenate = minimumCharacterCountToLineBreak * criteria.FontSize.X;

            if (criteria.MaximumPixelWidth >= minimumWordSizeToHyphenate)
            {
                WordWrapWithHyphenation(displayText, criteria, minimumWordSizeToHyphenate);
            }
            else
            {
                SimpleWordWrap(displayText, criteria);
            }
        }

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
        
        private static void WordWrapWithHyphenation(DisplayText displayText, FormatTextCriteria criteria, float minimumWordSizeToLineBreak)
        {
            foreach (var textLine in criteria.TextLinesToFormat)
            {
                var words = textLine.Split(' ');
                var currentLineStringBuilder = new StringBuilder();
                var remainingLineWidth = criteria.MaximumPixelWidth;

                foreach (var word in words)
                {
                    if (remainingLineWidth != criteria.MaximumPixelWidth)
                    {
                        currentLineStringBuilder.Append(' ');
                        remainingLineWidth -= criteria.FontSize.X;
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
                            var remainingHyphenatedLineWidth = remainingLineWidth - criteria.FontSize.X;

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
