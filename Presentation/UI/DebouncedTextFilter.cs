namespace Presentation.UI;

/// <summary>
/// Debounces a rapidly-changing text value (e.g. a search box's live OriginalText) into a stable
/// "applied" value once it's sat unchanged for debounceFrames consecutive Update calls -- the
/// same delay-gated idiom HudMetrics.HoverTooltipDelayFrames already establishes for this
/// codebase's hover popups, just keyed off typing instead of hovering. TabbedContent's own tab
/// search was the first consumer of this exact logic; GridControl's item search is the second,
/// which is what pulled it out into its own reusable type.
/// </summary>
public sealed class DebouncedTextFilter(int debounceFrames)
{
    private string _lastSeenText = string.Empty;
    private int _framesUnchanged;

    public string AppliedText { get; private set; } = string.Empty;

    /// <summary>
    /// Call once per frame with the current live text. Returns true exactly the frame
    /// AppliedText changes as a result -- i.e. the text has now sat unchanged for
    /// debounceFrames frames and differs from what was last applied -- false every other frame,
    /// including while the text is still actively changing.
    /// </summary>
    public bool Update(string currentText)
    {
        if (currentText != _lastSeenText)
        {
            _lastSeenText = currentText;
            _framesUnchanged = 0;
            return false;
        }

        if (currentText == AppliedText)
        {
            return false;
        }

        _framesUnchanged++;
        if (_framesUnchanged < debounceFrames)
        {
            return false;
        }

        AppliedText = currentText;
        return true;
    }
}
