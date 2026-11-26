using Presentation.UI.Details;
using UIEngine.UI;
using UIEngine.UI.Content;

namespace Presentation.UI.Actions;

/// <summary>An Actions window cell under the pointer: the action or menu command it shows, and its name.</summary>
public readonly record struct ActionHoverTarget(ActionCellSubject Subject, string Name) : IHoverTarget<ActionHoverTarget>
{
    public bool IsSameCellAs(in ActionHoverTarget other) => Subject == other.Subject;
}

/// <summary>The Action Details popup as hovering sees it, its width taken from the Actions window a cell was hovered in.</summary>
public interface IActionDetailsPopup : IDetailsPopup<ActionHoverTarget, Window>
{
    /// <summary>Starts compare mode on the player's action actionId -- an action cell's "Compare".</summary>
    void Compare(Guid actionId);
}

/// <summary>What hovering an Actions window cell shows (HoverDetails): its name above the cursor at once, then Action Details at the cursor after the delay -- Item Details' rule.</summary>
public sealed class ActionHoverDetails(CursorTextContent cursorLabel) : HoverDetails<ActionHoverTarget, Window, IActionDetailsPopup>(cursorLabel)
{
    /// <summary>An action cell's "Compare": starts compare mode on the player's action actionId.</summary>
    public void Compare(Guid actionId) => Popup?.Compare(actionId);
}
