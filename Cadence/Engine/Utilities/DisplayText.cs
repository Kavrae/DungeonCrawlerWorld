namespace Engine.Utilities;

/// <summary>Formatted display text with metadata</summary>
/// <cleanupVersion>1</cleanupVersion>
public readonly struct DisplayText(string formattedText, int lineCount)
{
    /// <summary>The formatted text</summary>
    public string FormattedText { get; } = formattedText.TrimEnd('\r', '\n');

    /// <summary>Number of text lines to be displayed, based on inserted newlines.</summary>
    public int LineCount { get; } = lineCount;
}