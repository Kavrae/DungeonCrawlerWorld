using Engine.Utilities;

namespace Tests.Utilities;

[TestClass]
public sealed class StringUtilityTests
{
    /// <summary>Fixed pixel width per character, so wrap/truncate math is hand-verifiable in tests.</summary>
    private sealed class FixedWidthTextMeasurer(float widthPerCharacter) : ITextMeasurer
    {
        public float MeasureWidth(string text) => text.Length * widthPerCharacter;
    }

    [TestMethod]
    public void BuildPercentageBar_HalfFilled_FillsHalfTheBar()
    {
        var bar = StringUtility.BuildPercentageBar(prefix: "HP", currentValue: 50, maximumValue: 100, barSize: 10);

        Assert.AreEqual("HP : [=====_____]", bar);
    }

    [TestMethod]
    public void BuildPercentageBar_ZeroCurrentValue_FillsNothing()
    {
        var bar = StringUtility.BuildPercentageBar(prefix: "HP", currentValue: 0, maximumValue: 100, barSize: 10);

        Assert.AreEqual("HP : [__________]", bar);
    }

    [TestMethod]
    public void BuildPercentageBar_FullValue_FillsEverything()
    {
        var bar = StringUtility.BuildPercentageBar(prefix: "HP", currentValue: 100, maximumValue: 100, barSize: 10);

        Assert.AreEqual("HP : [==========]", bar);
    }

    [TestMethod]
    public void BuildPercentageBar_ShorterPrefix_UsesItInPlaceOfHP()
    {
        var bar = StringUtility.BuildPercentageBar(prefix: "E", currentValue: 50, maximumValue: 100, barSize: 10);

        Assert.AreEqual("E : [=====_____]", bar);
    }

    [TestMethod]
    public void BuildPercentageBar_LongerPrefix_UsesItInPlaceOfHP()
    {
        var bar = StringUtility.BuildPercentageBar(prefix: "Stamina", currentValue: 50, maximumValue: 100, barSize: 10);

        Assert.AreEqual("Stamina : [=====_____]", bar);
    }

    [TestMethod]
    public void BuildPercentageBar_NullPrefix_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => StringUtility.BuildPercentageBar(null!, 50, 100, 10));
    }

    [TestMethod]
    public void BuildPercentageBar_EmptyPrefix_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => StringUtility.BuildPercentageBar("", 50, 100, 10));
    }

    [TestMethod]
    public void FormatText_WhitespaceOnly_ReturnsEmptyDisplayText()
    {
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 100, "   ", FormatTextMode.Wordwrap);

        var result = StringUtility.FormatText(criteria);

        Assert.AreEqual(string.Empty, result.FormattedText);
        Assert.AreEqual(0, result.LineCount);
    }

    [TestMethod]
    public void FormatText_FitsWithinMaximumWidth_ReturnedUnchanged()
    {
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 100, "Hi", FormatTextMode.Wordwrap);

        var result = StringUtility.FormatText(criteria);

        Assert.AreEqual("Hi", result.FormattedText);
        Assert.AreEqual(1, result.LineCount);
    }

    [TestMethod]
    public void FormatText_TruncateMode_TruncatesToFitWidth()
    {
        // 10 chars * 10px = 100px wide; a 40px max should keep 40% of the characters.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 40, "ABCDEFGHIJ", FormatTextMode.Truncate);

        var result = StringUtility.FormatText(criteria);

        Assert.AreEqual("ABCD", result.FormattedText);
        Assert.AreEqual(1, result.LineCount);
    }

    [TestMethod]
    public void FormatText_WordwrapModeTooNarrowForHyphenation_UsesSimpleWordWrap()
    {
        // minimumWordSizeToHyphenate = 6 chars * 10px = 60px; a 30px max is below that,
        // so this must fall back to fixed-width chunking rather than word-boundary wrap.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 30, "ABCDEFGHI", FormatTextMode.Wordwrap);

        var result = StringUtility.FormatText(criteria);

        Assert.AreEqual($"ABC{Environment.NewLine}DEF{Environment.NewLine}GHI", result.FormattedText);
        Assert.AreEqual(4, result.LineCount);
    }

    [TestMethod]
    public void SimpleWordWrap_TextShorterThanOneChunk_ReturnedUnchanged()
    {
        // fullChunkCount == 0: must not divide/chunk by zero, and there's nothing to break.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 30, "AB", FormatTextMode.Wordwrap);

        var result = StringUtility.SimpleWordWrap(criteria);

        Assert.AreEqual("AB", result.FormattedText);
    }

    [TestMethod]
    public void SimpleWordWrap_MaximumWidthNarrowerThanOneCharacter_DoesNotThrow()
    {
        // lineLength would compute to 0 without the Math.Max(1, ...) guard.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 1, "ABCDEF", FormatTextMode.Wordwrap);

        var result = StringUtility.SimpleWordWrap(criteria);

        Assert.IsFalse(string.IsNullOrEmpty(result.FormattedText));
    }

    [TestMethod]
    public void FormatText_WordwrapMode_ConsecutiveSpacesDoNotBreakTheScan()
    {
        // Manual word-boundary scanning must include the empty segment between
        // consecutive spaces, matching string.Split(' ')'s behavior (no
        // RemoveEmptyEntries), so this must not throw or drop the second space.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 100, "A  B", FormatTextMode.Wordwrap);

        var result = StringUtility.FormatText(criteria);

        Assert.AreEqual("A  B", result.FormattedText);
    }

    [TestMethod]
    public void FormatText_WordwrapMode_BreaksOnWordBoundaryWhenAWordDoesNotFit()
    {
        // "Hello" (50px) fits; " World" doesn't fit in the remaining 50px, so it wraps as
        // a whole word onto the next line rather than being hyphenated.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 100, "Hello World", FormatTextMode.Wordwrap);

        var result = StringUtility.FormatText(criteria);

        Assert.AreEqual($"Hello {Environment.NewLine}World", result.FormattedText);
        Assert.AreEqual(2, result.LineCount);
    }

    [TestMethod]
    public void FormatText_WordwrapMode_HyphenatesAWordTooLongForOneLine()
    {
        // A single 10-char word (100px) against a 90px max: it can't fit on any one line,
        // is long enough (>=6 chars) and there's enough room (>=60px) to hyphenate, so it
        // splits with a trailing hyphen instead of overflowing or being pushed whole to a new line.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 90, "ABCDEFGHIJ", FormatTextMode.Wordwrap);

        var result = StringUtility.FormatText(criteria);

        Assert.AreEqual($"ABCDEFG-{Environment.NewLine}HIJ", result.FormattedText);
        Assert.AreEqual(2, result.LineCount);
    }

    [TestMethod]
    public void DisplayText_TrimsTrailingNewlineFromFormattedText()
    {
        var displayText = new DisplayText("original", "formatted\r\n", 2);

        Assert.AreEqual("formatted", displayText.FormattedText);
        Assert.AreEqual("original", displayText.OriginalText);
        Assert.AreEqual(2, displayText.LineCount);
    }

    /// <summary>Space is narrow but every other character is very wide -- reproduces a word
    /// whose real width is far larger than the space-based CharacterWidth estimate suggests.</summary>
    private sealed class VariableWidthTextMeasurer : ITextMeasurer
    {
        public float MeasureWidth(string text)
        {
            var width = 0f;
            foreach (var character in text)
            {
                width += character == ' ' ? 5f : 200f;
            }
            return width;
        }
    }

    [TestMethod]
    [Timeout(5000, CooperativeCancellation = true)]
    public void FormatText_WordMuchWiderThanCharacterWidthEstimate_TerminatesInsteadOfHanging()
    {
        // Regression test for a real infinite-loop risk: a word made of characters far
        // wider than the space-based CharacterWidth proxy can shrink below the
        // hyphenation character-count threshold while still not fitting even a freshly
        // reset full-width line. Without a forced-progress guard this loops forever,
        // growing the StringBuilder without bound. [Timeout] makes a regression fail
        // fast instead of hanging the whole test run.
        var criteria = new FormatTextCriteria(new VariableWidthTextMeasurer(), 40, "WWWWWWWWWW", FormatTextMode.Wordwrap);

        var result = StringUtility.FormatText(criteria);

        Assert.IsFalse(string.IsNullOrEmpty(result.FormattedText));
    }

    [TestMethod]
    public void Truncate_NegativeMaximumWidth_ClampsToEmptyInsteadOfThrowing()
    {
        // A collapsed/negative-width window is a real transient state during resize, not
        // just a theoretical one -- must degrade gracefully, not throw from the range slice.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), -5, "ABCDEFGHIJ", FormatTextMode.Truncate);

        var result = StringUtility.FormatText(criteria);

        Assert.AreEqual(string.Empty, result.FormattedText);
    }

    [TestMethod]
    public void BuildPercentageBar_ZeroMaximumValue_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StringUtility.BuildPercentageBar("HP", 0, 0, 10));
    }

    [TestMethod]
    public void BuildPercentageBar_NegativeMaximumValue_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StringUtility.BuildPercentageBar("HP", 5, -10, 10));
    }

    [TestMethod]
    public void BuildPercentageBar_NegativeBarSize_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => StringUtility.BuildPercentageBar("HP", 5, 10, -1));
    }

    [TestMethod]
    public void BuildPercentageBar_CurrentValueExceedsMaximum_ClampsToFullBar()
    {
        var bar = StringUtility.BuildPercentageBar(prefix: "HP", currentValue: 500, maximumValue: 100, barSize: 10);

        Assert.AreEqual("HP : [==========]", bar);
    }

    [TestMethod]
    public void BuildPercentageBar_NegativeCurrentValue_ClampsToEmptyBar()
    {
        var bar = StringUtility.BuildPercentageBar(prefix: "HP", currentValue: -50, maximumValue: 100, barSize: 10);

        Assert.AreEqual("HP : [__________]", bar);
    }

    [TestMethod]
    public void FormatTextCriteria_NullTextMeasurer_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new FormatTextCriteria(null!, 100, "text", FormatTextMode.Wordwrap));
    }

    [TestMethod]
    public void SimpleWordWrap_EmbeddedNewlineNotAtChunkBoundary_GetsSplitMidChunkInsteadOfRespected()
    {
        // Regression-documenting test, not a fix: SimpleWordWrap chunks by a fixed character
        // count with no awareness of an embedded '\n' -- a pre-existing newline (e.g. from a
        // future multiline text box's Shift+Enter) can land in the middle of a chunk instead
        // of starting its own line. Anything built on top of this that needs embedded
        // newlines respected has to route around it (e.g. wrap each '\n'-delimited segment
        // independently) rather than assume this handles them.
        var criteria = new FormatTextCriteria(new FixedWidthTextMeasurer(10), 50, "AB\nCDEFGHIJ", FormatTextMode.Wordwrap);

        var result = StringUtility.SimpleWordWrap(criteria);

        // If the newline were respected as a forced break, "AB" would be its own line.
        // Instead 5-character chunking swallows it into the middle of the first chunk.
        Assert.AreEqual($"AB\nCD{Environment.NewLine}EFGHI{Environment.NewLine}J", result.FormattedText);
    }
}