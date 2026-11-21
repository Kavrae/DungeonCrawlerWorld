namespace Presentation.UI.Inventory;

/// <summary>The mode the player chose for Item Details: Simple until the mode button switches it, kept for the session.</summary>
/// <remarks>One instance, shared by Item Details and the comparison popup (ItemWindowCoordinator), so closing and reopening keeps it. A comparison shows simple whatever it says (ItemDetailsWindow.ShownMode).</remarks>
public sealed class ItemDetailsModeSetting
{
    public DetailsMode Chosen { get; private set; } = DetailsMode.Simple;

    public void Toggle() => Chosen = Chosen == DetailsMode.Simple ? DetailsMode.Advanced : DetailsMode.Simple;
}
