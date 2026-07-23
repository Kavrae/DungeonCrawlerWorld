using Presentation.UI;

namespace Presentation.Input;

/// <summary>What kind of drag interaction (if any) a press on a window starts -- see WindowInteraction.</summary>
internal enum WindowInteractionKind
{
    None,
    Move,
    Resize,
}

/// <summary>
/// The result of Window.TryHitTestInteraction: which window (if any) was hit, what drag
/// interaction (if any) it starts, and -- for a button hit -- which button, so
/// GameInputController can track press/release state (Window Chrome Phase A0/B) from the same
/// hit-test that also drives raise-to-front and Move/Resize (Phase A1/C/D). Window is null
/// only when nothing was hit at all; it's still set (with Kind None) for a plain button/title/
/// content click, since that still needs to raise the window to front.
/// </summary>
internal readonly record struct WindowInteraction(WindowInteractionKind Kind, Window? Window, ResizeEdges Edges, Button? Button)
{
    public static readonly WindowInteraction NotHit = new(WindowInteractionKind.None, null, ResizeEdges.None, null);

    public static WindowInteraction ButtonClick(Window window, Button button) => new(WindowInteractionKind.None, window, ResizeEdges.None, button);

    public static WindowInteraction Click(Window window) => new(WindowInteractionKind.None, window, ResizeEdges.None, null);

    public static WindowInteraction Move(Window window) => new(WindowInteractionKind.Move, window, ResizeEdges.None, null);

    public static WindowInteraction Resize(Window window, ResizeEdges edges) => new(WindowInteractionKind.Resize, window, edges, null);
}