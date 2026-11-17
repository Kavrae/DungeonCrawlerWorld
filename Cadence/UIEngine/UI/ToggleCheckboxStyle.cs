namespace UIEngine.UI;

/// <summary>How a Toggle draws its checkbox.</summary>
public enum ToggleCheckboxStyle : byte
{
    /// <summary>A bevelled square in the toggle's on or off colour, raised while off and sunken while on, with a white cross when on.</summary>
    Raised,

    /// <summary>A smaller white square with a flat border and a dark cross when on -- a menu's checkbox row.</summary>
    Flat,
}
