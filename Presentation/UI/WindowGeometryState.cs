using Microsoft.Xna.Framework;

namespace Presentation.UI;

/// <summary>
/// A window's position/size/display-mode bookkeeping, grouped so a future chrome behavior
/// (e.g. a WindowMoveBehavior/WindowResizeBehavior attached the same way WindowCloseBehavior
/// is -- see IWindowChromeBehavior) has one cohesive surface to read/mutate instead of
/// reaching into several independent fields on Window itself. Plain mutable fields, not
/// properties -- Window and TextWindow do in-place mutation like `_geometry.CurrentSize.Y +=
/// ...`, which only compiles against a directly-addressable struct field.
///
/// This is the model for the wider *State naming convention (see also WindowTitleState,
/// WindowBorderState, WindowContentState, MapViewState): a type that exists purely as a
/// shared mutable bag of state for one or two designated writer/reader collaborators gets
/// plain fields, not properties, even where a given field isn't compound-mutated today --
/// it signals "mutable bag, not an encapsulated object with invariants" and keeps the
/// option open for compound mutation later without a later conversion.
/// </summary>
internal sealed class WindowGeometryState
{
    public WindowDisplayMode DisplayMode;
    public WindowDisplayMode PreviousDisplayMode;

    /// <summary>Position relative to the parent window's content area (or the screen, for a root window).</summary>
    public Vector2 RelativePosition;

    public Vector2 AbsolutePosition;
    public Vector2 CurrentSize;
    public Vector2 OriginalSize;
    public Vector2 MinimumSize;
    public Vector2 MaximumSize;
    public Rectangle Rectangle;
}