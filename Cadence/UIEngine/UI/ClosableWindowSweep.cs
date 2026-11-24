namespace UIEngine.UI;

/// <summary>Why every closable window is being closed at once, which decides whether windows that stay open through input are left.</summary>
public enum ClosableWindowSweep
{
    /// <summary>The player asked for every window closed (Escape held): every closable window closes.</summary>
    Everything,

    /// <summary>An input needs the windows out of its way (arming or using an action or item): a window with Window.StaysOpenWhenInputClearsWindows is left open.</summary>
    ForInput,
}
