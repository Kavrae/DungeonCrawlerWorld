using UIEngine.UI;

namespace Presentation.UI.Details;

/// <summary>A compare mode, as its input sees it (ComparisonInputHandler): whether it lasts, how to end it, and which cells it greys out.</summary>
public interface IComparisonMode
{
    bool IsComparing { get; }

    /// <summary>Ends compare mode and closes its popups -- a no-op outside it.</summary>
    void ClearComparison();

    /// <summary>Whether hoveredElement is a cell compare mode greyed out, which shows the refusal cursor.</summary>
    bool IsRefusedAt(Element? hoveredElement);
}
