using UIEngine.UI.Content;

namespace Presentation.UI;

/// <summary>The cursor text for putting things down: "Drop" while a drop is armed, "Add" while it is over a container it would go into, or what a held drag would do instead -- "Bind" over a hotbar slot, "Unbind" off one.</summary>
/// <remarks>
/// The one owner of these words on the shared CursorTextContent: it shows its own persistent text and hides only what it showed,
/// so another persistent message (Item Details Comparison's) isn't ended by a drop it never saw. Follows the armed drop through
/// ActionTargetingController.ArmedDropChanged and what it is over through SetArmedDropOverContainer (ArmedDropHover); a held
/// drag sets its own text through SetDragText (DragDropArming).
/// </remarks>
public sealed class DropCursorText
{
    public const string DropText = "Drop";

    public const string AddText = "Add";

    public const string UnbindText = "Unbind";

    public const string BindText = "Bind";

    private readonly CursorTextContent _cursorText;
    private readonly MapViewState _mapViewState;
    private string? _dragText;
    private bool _armedDropOverContainer;
    private string? _shownText;

    public DropCursorText(CursorTextContent cursorText, ActionTargetingController actionTargeting, MapViewState mapViewState)
    {
        _cursorText = cursorText;
        _mapViewState = mapViewState;
        actionTargeting.ArmedDropChanged += OnArmedDropChanged;
    }

    /// <summary>What a held drag shows in place of the armed drop's text; null to show what the armed drop alone would.</summary>
    public void SetDragText(string? text)
    {
        _dragText = text;
        Refresh();
    }

    /// <summary>Whether the armed drop is over a container it would go into, so it reads "Add" rather than "Drop".</summary>
    public void SetArmedDropOverContainer(bool isOverContainer)
    {
        _armedDropOverContainer = isOverContainer;
        Refresh();
    }

    private void OnArmedDropChanged()
    {
        if (_mapViewState.ArmedDrop is null)
        {
            _armedDropOverContainer = false;
        }

        Refresh();
    }

    private void Refresh()
    {
        var text = _dragText ?? (_mapViewState.ArmedDrop is null ? null : _armedDropOverContainer ? AddText : DropText);
        if (text == _shownText)
        {
            return;
        }

        _shownText = text;
        if (text is null)
        {
            _cursorText.HidePersistent();
            return;
        }

        _cursorText.ShowPersistent(text);
    }
}
