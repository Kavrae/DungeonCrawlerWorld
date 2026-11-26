namespace Presentation.UI.Content;

/// <summary>How a grid draws its cells: Icon -- a square, the icon filling it, its numbers in the corners -- or Card -- a wide row, the icon at its left, then the name and its numbers.</summary>
public enum GridView : byte
{
    Icon,
    Card,
}

/// <summary>The view one kind of window's grids show, chosen with its "Card view" switch and held for the session -- never saved in content.</summary>
/// <remarks>One instance per window kind (GridViewSettings), so every tab of a window, and a window opened again, shows the view last chosen. A grid reads it each update and rebuilds its cells when it changes.</remarks>
public sealed class GridViewSetting(GridView initial)
{
    public GridView Chosen { get; private set; } = initial;

    public bool ShowsCards => Chosen == GridView.Card;

    public void Choose(GridView view) => Chosen = view;
}

/// <summary>The session's grid views, one per window kind: the Inventory window's, the Actions window's and the shop's -- whose choice the player's inventory follows while a shop is open, since a trade compares one side with the other.</summary>
public sealed record GridViewSettings(GridViewSetting Inventory, GridViewSetting Actions, GridViewSetting Shop)
{
    /// <summary>Icon for the Inventory and Actions windows, Card for the shop: what each showed before the switch existed.</summary>
    public static GridViewSettings CreateDefault() => new(new GridViewSetting(GridView.Icon), new GridViewSetting(GridView.Icon), new GridViewSetting(GridView.Card));
}
