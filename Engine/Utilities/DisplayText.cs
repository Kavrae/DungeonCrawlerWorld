namespace Engine.Utilities;

/// <summary>Formatted display text with metadata, produced by StringUtility.FormatText.</summary>
public readonly struct DisplayText(string originalText, string formattedText, int lineCount)
{
    public string FormattedText { get; } = formattedText.TrimEnd('\r', '\n');

    public string OriginalText { get; } = originalText;

    /// <summary>Number of text lines to be displayed, based on inserted newlines.</summary>
    public int LineCount { get; } = lineCount;
}