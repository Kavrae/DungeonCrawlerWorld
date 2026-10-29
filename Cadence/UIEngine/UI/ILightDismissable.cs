namespace UIEngine.UI;

/// <summary>Something open that a press outside it closes -- a dropdown, a details window tied to the windows that opened it.</summary>
/// <remarks>
/// Opened on UiLayerStack (OpenLightDismiss) and closed there when it goes. While it is the topmost one open,
/// UiInputController hit-tests each press: when neither the element hit nor any of its ancestors counts as inside, the
/// press dismisses it and goes no further. Inside is decided by what was hit, never by rectangles, so a window drawn over
/// an inside one is outside.
/// </remarks>
public interface ILightDismissable
{
    bool IsOpen { get; }

    /// <summary>Whether a press on element, or on anything inside it, leaves this open.</summary>
    bool CountsAsInside(Element element);

    void Dismiss();
}
