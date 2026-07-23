using Engine.Math;
using System.Text;

namespace Engine.Utilities;

/// <summary>
/// Formatting and manipulation for strings displayed in the UI. Performance-sensitive --
/// called frequently during UI rendering.
/// </summary>
public static class StringUtility
{
    /// <summary>Words shorter than this are pushed to the next line instead of being hyphenated.</summary>
    private const int MinimumCharacterCountToLineBreak = 6;

    /// <summary>Minimum characters that must appear before a hyphenated line break.</summary>
    private const int MinimumCharactersBeforeLineBreak = 3;

    /// <summary>Minimum characters that must appear after a hyphenated line break.</summary>
    private const int MinimumCharactersAfterLineBreak = MinimumCharacterCountToLineBreak - MinimumCharactersBeforeLineBreak;

    /// <summary>Builds a UI bar of the given size and prefix (e.g. "HP", "E"), filled to a percentage of currentValue/maximumValue.</summary>
    public static string BuildPercentageBar(string prefix, int currentValue, int maximumValue, int barSize)
    {
        // maximumValue <= 0 would divide by zero below (an unspecified, garbage fillCount
        // rather than a clean failure); a negative barSize would write the prefix/bracket
        // characters past the end of a too-small span and crash with an unhelpful
        // IndexOutOfRangeException instead of a clear one here at the actual call site.
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumValue);
        ArgumentOutOfRangeException.ThrowIfNegative(barSize);

        // Clamp defensively: currentValue outside [0, maximumValue] (e.g. a heal that
        // overshoots max, or a stale negative value) must not produce a fill count outside
        // [0, barSize].
        var fillCount = currentValue == 0
            ? 0
            : MathUtility.ClampInt((int)((float)barSize * currentValue / maximumValue), 0, barSize);

        // total length: "{prefix}" + " : [" (4) + barSize chars + "]" (1)
        var barsStart = prefix.Length + 4;
        var spanSize = barsStart + barSize + 1;

        // Everything the fill routine needs travels through state so the lambda can be
        // static -- otherwise closing over prefix/barSize/etc. from this scope would
        // allocate a display class on every call despite using the TState overload
        // specifically to avoid that. Passing the prefix string itself through state is not
        // a closure allocation -- it's an existing reference, not a captured local.
        return string.Create(spanSize, (prefix, barSize, fillCount, barsStart, spanSize), static (span, state) =>
        {
            var (prefixValue, size, fill, start, totalSize) = state;

            prefixValue.AsSpan().CopyTo(span);
            span[prefixValue.Length] = ' '; span[prefixValue.Length + 1] = ':'; span[prefixValue.Length + 2] = ' '; span[prefixValue.Length + 3] = '[';

            for (var i = 0; i < size; i++)
            {
                span[start + i] = i < fill
                    ? '='
                    : '_';
            }

            span[totalSize - 1] = ']';
        });
    }

    /// <summary>Formats text per the given criteria, truncating or word-wrapping as needed.</summary>
    public static DisplayText FormatText(FormatTextCriteria criteria)
    {
        if (string.IsNullOrWhiteSpace(criteria.OriginalText))
        {
            return new DisplayText(string.Empty, string.Empty, 0);
        }

        if (criteria.TextWidth <= criteria.MaximumPixelWidth)
        {
            return new DisplayText(criteria.OriginalText, criteria.OriginalText, CountNewlineCharacters(criteria.OriginalText));
        }

        return criteria.FormatTextMode == FormatTextMode.Wordwrap
            ? WordWrap(criteria)
            : Truncate(criteria);
    }

    /// <summary>
    /// Truncates text to fit within the maximum pixel width. Rounds toward zero to avoid
    /// characters running past the maximum width.
    /// </summary>
    private static DisplayText Truncate(FormatTextCriteria criteria)
    {
        var percentageDifference = criteria.MaximumPixelWidth / criteria.TextWidth;

        // Clamped, not a raw cast: a collapsed or negative-width window (a real transient
        // state during resize, not just a theoretical one) drives percentageDifference
        // negative, which would otherwise produce a negative substring length and throw
        // from the range slice below instead of degrading to an empty/short string.
        var substringLength = MathUtility.ClampInt(
            (int)System.Math.Round(percentageDifference * criteria.OriginalText.Length, MidpointRounding.ToZero),
            0,
            criteria.OriginalText.Length);

        return new DisplayText(criteria.OriginalText, criteria.OriginalText[..substringLength], 1);
    }

    /// <summary>Small text boxes don't have room for hyphenation and use a simpler word wrap.</summary>
    private static DisplayText WordWrap(FormatTextCriteria criteria)
    {
        var minimumWordSizeToHyphenate = MinimumCharacterCountToLineBreak * criteria.CharacterWidth;

        return criteria.MaximumPixelWidth >= minimumWordSizeToHyphenate
            ? WordWrapWithHyphenation(criteria, minimumWordSizeToHyphenate)
            : SimpleWordWrap(criteria);
    }

    /// <summary>A simple, more space-efficient word wrap based on character pixel widths, ignoring word boundaries.</summary>
    public static DisplayText SimpleWordWrap(FormatTextCriteria criteria)
    {
        // Minimum 1: a window narrower than one character would otherwise divide by (or chunk by) zero.
        var lineLength = System.Math.Max(1, (int)(criteria.MaximumPixelWidth / criteria.CharacterWidth));
        var formattedText = InsertLineBreakEveryNCharacters(criteria.OriginalText, lineLength);
        return new DisplayText(criteria.OriginalText, formattedText, CountNewlineCharacters(formattedText));
    }

    /// <summary>
    /// Inserts a line break after every full chunk of lineLength characters (a trailing
    /// partial chunk shorter than lineLength is left without a following break). A pre-sized
    /// string.Create fill replaces what was previously a regex compiled fresh per call --
    /// the pattern varies with lineLength (window size/font dependent), which defeated
    /// .NET's regex cache far more than a plain UI-text-formatting call should.
    /// </summary>
    private static string InsertLineBreakEveryNCharacters(string text, int lineLength)
    {
        var fullChunkCount = text.Length / lineLength;
        if (fullChunkCount == 0)
        {
            return text;
        }

        var lineBreak = Environment.NewLine;
        var outputLength = text.Length + fullChunkCount * lineBreak.Length;

        return string.Create(outputLength, (text, lineLength, fullChunkCount, lineBreak), static (destination, state) =>
        {
            var (sourceText, chunkLength, chunkCount, sourceLineBreak) = state;
            var sourceIndex = 0;
            var destinationIndex = 0;

            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                sourceText.AsSpan(sourceIndex, chunkLength).CopyTo(destination[destinationIndex..]);
                destinationIndex += chunkLength;
                sourceIndex += chunkLength;

                sourceLineBreak.AsSpan().CopyTo(destination[destinationIndex..]);
                destinationIndex += sourceLineBreak.Length;
            }

            sourceText.AsSpan(sourceIndex).CopyTo(destination[destinationIndex..]);
        });
    }

    /// <summary>
    /// Word-wraps on word boundaries first; a word exceeding the maximum pixel width is
    /// hyphenated and split across lines based on the minimum character counts before/after
    /// the break.
    /// </summary>
    private static DisplayText WordWrapWithHyphenation(FormatTextCriteria criteria, float minimumWordSizeToLineBreak)
    {
        var originalText = criteria.OriginalText;
        // Output is rarely far from the input length (a handful of newlines/hyphens added);
        // sizing up front avoids StringBuilder's internal buffer regrowth on this hot path.
        var stringBuilder = new StringBuilder(originalText.Length + 16);
        var remainingLineWidth = criteria.MaximumPixelWidth;

        // Walk space-delimited words directly (matching string.Split(' ')'s inclusion of
        // empty entries between consecutive spaces) instead of materializing the whole
        // words array up front -- avoids that one array allocation per call. Each word
        // still becomes its own string when sliced out below: FontStashSharp's
        // MeasureString has no ReadOnlySpan<char> overload (checked directly against the
        // installed package), so a per-word string is unavoidable regardless of how the
        // words are found.
        var wordStartIndex = 0;
        while (true)
        {
            var nextSpaceIndex = originalText.IndexOf(' ', wordStartIndex);
            var word = nextSpaceIndex < 0
                ? originalText[wordStartIndex..]
                : originalText[wordStartIndex..nextSpaceIndex];

            if (remainingLineWidth != criteria.MaximumPixelWidth)
            {
                stringBuilder.Append(' ');
                remainingLineWidth -= criteria.CharacterWidth;
            }

            var remainingWord = word;
            do
            {
                // A fresh line means we already reset and retried once for this remainder --
                // if it still can't fit or hyphenate below, retrying again would repeat the
                // exact same measurement and decision forever. This can genuinely happen: a
                // word whose actual glyphs are wider than the space-based CharacterWidth
                // estimate (e.g. a wide/CJK character) can shrink below the hyphenation
                // character-count threshold while still not fitting even a full-width line.
                var isFreshLine = remainingLineWidth == criteria.MaximumPixelWidth;
                var wordSize = criteria.TextMeasurer.MeasureWidth(remainingWord);

                if (remainingLineWidth > wordSize)
                {
                    stringBuilder.Append(remainingWord);
                    remainingLineWidth -= wordSize;
                    break;
                }

                var remainingHyphenatedLineWidth = remainingLineWidth - criteria.CharacterWidth;
                var canHyphenate = remainingHyphenatedLineWidth >= minimumWordSizeToLineBreak && remainingWord.Length >= MinimumCharacterCountToLineBreak;

                if (canHyphenate)
                {
                    var percentageOfWordThatFits = remainingHyphenatedLineWidth / wordSize;
                    var substringLength = MathUtility.ClampInt(
                        (int)System.Math.Round(percentageOfWordThatFits * remainingWord.Length, MidpointRounding.ToZero),
                        MinimumCharactersBeforeLineBreak,
                        remainingWord.Length - MinimumCharactersAfterLineBreak
                    );
                    var hyphenatedSubstring = string.Concat(remainingWord[..substringLength], "-");
                    stringBuilder.Append(hyphenatedSubstring);

                    // Estimate the hyphenated substring's width proportionally from the
                    // already-measured wordSize instead of calling MeasureWidth again --
                    // the substringLength above already came from the same proportional
                    // assumption (uniform-ish character width), so re-measuring the exact
                    // substring buys precision the rest of this calculation doesn't have
                    // either. The hyphen itself is approximated as one CharacterWidth,
                    // consistent with how a leading space is costed above.
                    var hyphenatedSubstringWidthEstimate = wordSize * substringLength / remainingWord.Length;
                    remainingLineWidth -= hyphenatedSubstringWidthEstimate + criteria.CharacterWidth;
                    remainingWord = remainingWord[substringLength..];
                }
                else if (isFreshLine)
                {
                    // Doesn't fit and can't be hyphenated even on a full fresh line: force
                    // it through instead of resetting and retrying identically forever.
                    // Guarantees termination at the cost of a rare visual overflow, which is
                    // strictly better than hanging or growing the StringBuilder unbounded.
                    stringBuilder.Append(remainingWord);
                    remainingLineWidth -= wordSize;
                    break;
                }
                else
                {
                    // Not enough space on this partially-used line -- try a fresh line
                    // before giving up (a full line may fit or be hyphenatable even if the
                    // remainder of this one wasn't).
                    stringBuilder.Append(Environment.NewLine);
                    remainingLineWidth = criteria.MaximumPixelWidth;
                }
            }
            while (remainingWord.Length > 0);

            if (nextSpaceIndex < 0)
            {
                break;
            }
            wordStartIndex = nextSpaceIndex + 1;
        }

        var formattedText = stringBuilder.ToString();
        return new DisplayText(criteria.OriginalText, formattedText, CountNewlineCharacters(formattedText));
    }

    /// <summary>
    /// Counts newline characters in the text. Used instead of counting lines produced by
    /// formatting, since text can arrive with pre-existing newlines for forced breaks.
    /// </summary>
    private static int CountNewlineCharacters(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return 1 + text.AsSpan().Count('\n');
    }
}