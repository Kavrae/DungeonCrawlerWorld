namespace Engine.Utilities;

public enum FormatTextMode
{
    Truncate,
    Wordwrap,
}

/// <summary>Input for StringUtility.FormatText, describing how a string should be formatted for display.</summary>
public readonly struct FormatTextCriteria
{
    /// <summary>Used to determine how many words/lines fit within the maximum pixel width for a line.</summary>
    public ITextMeasurer TextMeasurer { get; }

    /// <summary>The width of a single space character in pixels, per TextMeasurer.</summary>
    public float CharacterWidth { get; }

    /// <summary>The maximum width in pixels that a formatted line should occupy.</summary>
    public float MaximumPixelWidth { get; }

    public string OriginalText { get; }

    /// <summary>The width of the full, unwrapped text in pixels, per TextMeasurer.</summary>
    public float TextWidth { get; }

    /// <summary>
    /// Truncate: text exceeding the maximum pixel width is truncated. Wordwrap: text is
    /// wrapped onto multiple lines -- small boxes get a simple wrap, larger boxes get
    /// hyphenation and smarter line breaks.
    /// </summary>
    public FormatTextMode FormatTextMode { get; }

    public FormatTextCriteria(ITextMeasurer textMeasurer, float maximumPixelWidth, string originalText, FormatTextMode formatTextMode)
    {
        ArgumentNullException.ThrowIfNull(textMeasurer);

        TextMeasurer = textMeasurer;
        MaximumPixelWidth = maximumPixelWidth;
        OriginalText = originalText;
        FormatTextMode = formatTextMode;
        CharacterWidth = textMeasurer.MeasureWidth(" ");
        TextWidth = textMeasurer.MeasureWidth(originalText);
    }
}