using Microsoft.Xna.Framework;

namespace UIEngine.Input;

/// <summary>One mouse button's press: where it started and whether it has become a drag.</summary>
/// <remarks>
/// The one tap-versus-drag rule for every gesture UiInputController recognises -- a content drag, a text selection, a
/// right-button pan, a handler's tap. A press becomes a drag the moment the cursor is TapThresholdPixels or more from
/// where it was pressed, and stays one: coming back to the press point before releasing is still a drag.
/// </remarks>
internal sealed class PointerGesture
{
    /// <summary>Small enough to absorb ordinary click jitter, comfortably smaller than an intentional drag.</summary>
    internal const float TapThresholdPixels = 4f;

    /// <summary>Where the button was pressed.</summary>
    public Point PressPoint { get; private set; }

    /// <summary>PressPoint as a vector, for deltas.</summary>
    public Vector2 PressPosition => new(PressPoint.X, PressPoint.Y);

    /// <summary>Whether the cursor has been TapThresholdPixels or more from PressPosition since the press.</summary>
    public bool IsDrag { get; private set; }

    /// <summary>Starts a new press at position.</summary>
    public void Press(Point position)
    {
        PressPoint = position;
        IsDrag = false;
    }

    /// <summary>The cursor at position while the button is held, or at its release; returns true the call it becomes a drag.</summary>
    public bool Track(Point position)
    {
        if (IsDrag || Vector2.Distance(PressPosition, new Vector2(position.X, position.Y)) < TapThresholdPixels)
        {
            return false;
        }

        IsDrag = true;
        return true;
    }
}
