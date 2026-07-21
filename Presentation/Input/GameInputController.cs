using Engine.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;

namespace Presentation.Input;

/// <summary>
/// Translates raw keyboard/mouse state into the app's UI-level interactions
/// </summary>
public sealed class GameInputController(MapWindow mapWindow, List<Window> rootWindows, List<Window> alwaysOnTopWindows, Vector2 screenSize)
{
    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;
    private ZoomLevel _currentZoomLevel = ZoomLevel.Team;

    private WindowInteraction _activeInteraction = WindowInteraction.NotHit;
    private Vector2 _dragStartMousePosition;
    private Vector2 _dragStartRelativePosition;
    private Vector2 _dragStartSize;

    public bool IsPaused { get; private set; }

    /// <summary>
    /// The title button currently held down by the mouse, if any -- null the rest of the
    /// time, including once the matching release fires (regardless of where the mouse ends
    /// up). Window Chrome Phase B reads this to switch the held button to an Inset look.
    /// </summary>
    internal Button? PressedButton { get; private set; }

    /// <summary>The drag/resize interaction currently in progress (or WindowInteraction.NotHit if none). Move is wired to SetRelativePosition and Resize to SetBounds, both each held frame -- see ComputeResize for the resize math.</summary>
    internal WindowInteraction ActiveInteraction => _activeInteraction;

    /// <summary>Window.WindowRelativePosition captured at the start of the current drag -- meaningless when ActiveInteraction.Kind is None. Move's per-frame SetRelativePosition is this plus DragDelta; ComputeResize uses it as the Left/Top-edge resize baseline.</summary>
    internal Vector2 DragStartRelativePosition => _dragStartRelativePosition;

    /// <summary>Window.WindowCurrentSize captured at the start of the current drag -- meaningless when ActiveInteraction.Kind is None. ComputeResize's resize baseline.</summary>
    internal Vector2 DragStartSize => _dragStartSize;

    /// <summary>Mouse movement since the drag started, recomputed every held frame -- zero on the press frame itself and once released.</summary>
    internal Vector2 DragDelta { get; private set; }

    /// <summary>The last cursor UpdateCursor set (or the initial Arrow default, if it's never had reason to change) -- lets tests assert on cursor selection without depending on real OS cursor state.</summary>
    internal MouseCursor CurrentCursor { get; private set; } = MouseCursor.Arrow;

    public void Update(GameTime gameTime) => Update(Keyboard.GetState(), Mouse.GetState());

    /// <summary>
    /// Takes explicit states rather than reading Keyboard.GetState()/Mouse.GetState() itself,
    /// so tests can drive synthetic press/release/move sequences -- the public
    /// Update(GameTime) overload above is the only real caller otherwise.
    /// </summary>
    internal void Update(KeyboardState keyboardState, MouseState mouseState)
    {
        if (IsKeyPressed(keyboardState, Keys.Space))
        {
            IsPaused = !IsPaused;
        }

        var scrollChange = Point.Zero;
        if (keyboardState.IsKeyDown(Keys.W))
        {
            scrollChange.Y -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.S))
        {
            scrollChange.Y += 1;
        }
        if (keyboardState.IsKeyDown(Keys.A))
        {
            scrollChange.X -= 1;
        }
        if (keyboardState.IsKeyDown(Keys.D))
        {
            scrollChange.X += 1;
        }
        if (scrollChange != Point.Zero)
        {
            mapWindow.UpdateScrollPosition(scrollChange);
        }

        if (IsKeyPressed(keyboardState, Keys.OemPlus) || IsKeyPressed(keyboardState, Keys.Add))
        {
            CycleZoom(-1);
        }
        if (IsKeyPressed(keyboardState, Keys.OemMinus) || IsKeyPressed(keyboardState, Keys.Subtract))
        {
            CycleZoom(1);
        }

        if (IsKeyPressed(keyboardState, Keys.PageUp))
        {
            mapWindow.ChangeLayer(1);
        }
        if (IsKeyPressed(keyboardState, Keys.PageDown))
        {
            mapWindow.ChangeLayer(-1);
        }

        if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released)
        {
            var clickPosition = new Point(mouseState.X, mouseState.Y);

            _activeInteraction = TryHitTestInteraction(clickPosition);
            PressedButton = _activeInteraction.Button;
            PressedButton?.SetPressed(true);
            DragDelta = Vector2.Zero;

            if (_activeInteraction.Window is not null)
            {
                RaiseToFront(_activeInteraction.Window);

                if (_activeInteraction.Kind != WindowInteractionKind.None)
                {
                    _dragStartMousePosition = new Vector2(mouseState.X, mouseState.Y);
                    _dragStartRelativePosition = _activeInteraction.Window.WindowRelativePosition;
                    _dragStartSize = _activeInteraction.Window.WindowCurrentSize;
                }
            }
        }
        else if (mouseState.LeftButton == ButtonState.Released && _previousMouseState.LeftButton == ButtonState.Pressed)
        {
            // Fires on release, not press -- standard button convention (press only starts
            // the pressed visual above; release commits, re-hit-testing the same window at
            // the release position so a button/title/content click that's been dragged off
            // its target quietly does nothing rather than firing against whatever else the
            // mouse happens to be over). This is also the only way the pressed visual is ever
            // actually observable: firing on press meant a destructive action (Close) usually
            // destroyed the button before a held frame could even render.
            _activeInteraction.Window?.HandleClick(new Point(mouseState.X, mouseState.Y));

            PressedButton?.SetPressed(false);
            PressedButton = null;
            _activeInteraction = WindowInteraction.NotHit;
            DragDelta = Vector2.Zero;
        }
        else if (mouseState.LeftButton == ButtonState.Pressed && _activeInteraction.Kind != WindowInteractionKind.None)
        {
            // Held, mid-drag.
            DragDelta = new Vector2(mouseState.X, mouseState.Y) - _dragStartMousePosition;

            if (_activeInteraction.Kind == WindowInteractionKind.Move && _activeInteraction.Window is not null)
            {
                var window = _activeInteraction.Window;
                var desiredPosition = _dragStartRelativePosition + DragDelta;
                window.SetRelativePosition(ClampMoveToBounds(desiredPosition, window.WindowCurrentSize, GetPositionBounds(window)));
            }
            else if (_activeInteraction.Kind == WindowInteractionKind.Resize && _activeInteraction.Window is not null)
            {
                var window = _activeInteraction.Window;
                var (relativePosition, size) = ComputeResize(window, _activeInteraction.Edges, _dragStartRelativePosition, _dragStartSize, DragDelta);
                (relativePosition, size) = ClampResizeToBounds(relativePosition, size, GetPositionBounds(window));
                window.SetBounds(relativePosition, size);
            }
        }

        UpdateCursor(mouseState);

        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;
    }

    /// <summary>Always-on-top tier first (so it can never lose to a root window), each tier topmost (last-raised) first.</summary>
    private WindowInteraction TryHitTestInteraction(Point position)
    {
        var interaction = TryHitTestInList(alwaysOnTopWindows, position);
        return interaction.Window is not null ? interaction : TryHitTestInList(rootWindows, position);
    }

    private static WindowInteraction TryHitTestInList(List<Window> windows, Point position)
    {
        for (var index = windows.Count - 1; index >= 0; index--)
        {
            var interaction = windows[index].TryHitTestInteraction(position);
            if (interaction.Window is not null)
            {
                return interaction;
            }
        }

        return WindowInteraction.NotHit;
    }

    /// <summary>
    /// Raises window within its own parent's children (no-op if it has no parent -- see
    /// Window.RaiseToFront), then raises whichever top-level ancestor contains it within its
    /// own tier (rootWindows/alwaysOnTopWindows), so the whole subtree ends up drawn/hit-tested
    /// on top of its siblings at every level.
    /// </summary>
    private void RaiseToFront(Window window)
    {
        window.RaiseToFront();

        var rootAncestor = window;
        while (rootAncestor.ParentWindow is not null)
        {
            rootAncestor = rootAncestor.ParentWindow;
        }

        if (rootWindows.Remove(rootAncestor))
        {
            rootWindows.Add(rootAncestor);
        }
        else if (alwaysOnTopWindows.Remove(rootAncestor))
        {
            alwaysOnTopWindows.Add(rootAncestor);
        }
    }

    /// <summary>
    /// Computes the relative position and size a resize drag should produce this frame. Right/
    /// Bottom grow the size directly (dragStartSize plus delta) with no position change.
    /// Left/Top must derive the position shift from the *actual clamped* size, not the raw
    /// drag delta -- otherwise the pinned (opposite) edge drifts once the drag exceeds
    /// WindowMinimumSize/WindowMaximumSize, since the position shift and the size shrink have
    /// to match exactly to keep that edge visually fixed. All four edges can combine (a corner
    /// drag sets two of them at once).
    /// </summary>
    private static (Vector2 RelativePosition, Vector2 Size) ComputeResize(Window window, ResizeEdges edges, Vector2 dragStartRelativePosition, Vector2 dragStartSize, Vector2 dragDelta)
    {
        var relativePosition = dragStartRelativePosition;
        var size = dragStartSize;

        if (edges.HasFlag(ResizeEdges.Right))
        {
            size.X = MathHelper.Clamp(dragStartSize.X + dragDelta.X, window.WindowMinimumSize.X, window.WindowMaximumSize.X);
        }
        if (edges.HasFlag(ResizeEdges.Bottom))
        {
            size.Y = MathHelper.Clamp(dragStartSize.Y + dragDelta.Y, window.WindowMinimumSize.Y, window.WindowMaximumSize.Y);
        }
        if (edges.HasFlag(ResizeEdges.Left))
        {
            var clampedWidth = MathHelper.Clamp(dragStartSize.X - dragDelta.X, window.WindowMinimumSize.X, window.WindowMaximumSize.X);
            relativePosition.X = dragStartRelativePosition.X + (dragStartSize.X - clampedWidth);
            size.X = clampedWidth;
        }
        if (edges.HasFlag(ResizeEdges.Top))
        {
            var clampedHeight = MathHelper.Clamp(dragStartSize.Y - dragDelta.Y, window.WindowMinimumSize.Y, window.WindowMaximumSize.Y);
            relativePosition.Y = dragStartRelativePosition.Y + (dragStartSize.Y - clampedHeight);
            size.Y = clampedHeight;
        }

        return (relativePosition, size);
    }

    /// <summary>
    /// The space a window's RelativePosition/Size are measured against: a root window's is the
    /// screen itself (RelativePosition doubles as its absolute screen position, see
    /// Window.BuildWindow), a child window's is its parent's own content area (RelativePosition
    /// is relative to ContentAbsolutePosition).
    /// </summary>
    private Vector2 GetPositionBounds(Window window) => window.ParentWindow?.ContentSize ?? screenSize;

    /// <summary>
    /// Pulls a drag-to-move's destination position back inside GetPositionBounds -- called with
    /// size unchanged, so a window dragged toward/past an edge simply stops there instead of
    /// continuing to follow the mouse off-screen (or out of its parent's content area).
    /// </summary>
    private static Vector2 ClampMoveToBounds(Vector2 position, Vector2 size, Vector2 bounds) => new(
        MathHelper.Clamp(position.X, 0, MathHelper.Max(0, bounds.X - size.X)),
        MathHelper.Clamp(position.Y, 0, MathHelper.Max(0, bounds.Y - size.Y)));

    /// <summary>
    /// Pulls a drag-to-resize's destination position+size back inside bounds. Unlike a move
    /// clamp (which only ever adjusts position), overflowing the left/top edge here must shrink
    /// the size by the overflow amount (not just clamp position) -- the window is being resized,
    /// not relocated, so running out of room at the edge being dragged should stop that edge
    /// growing further rather than sliding the whole window back on-screen. The right/bottom
    /// edges never move on their own (see ComputeResize), so overflow there is always a pure
    /// size reduction with no position change.
    /// </summary>
    private static (Vector2 RelativePosition, Vector2 Size) ClampResizeToBounds(Vector2 relativePosition, Vector2 size, Vector2 bounds)
    {
        if (relativePosition.X < 0)
        {
            size.X += relativePosition.X;
            relativePosition.X = 0;
        }
        if (relativePosition.Y < 0)
        {
            size.Y += relativePosition.Y;
            relativePosition.Y = 0;
        }

        var rightOverflow = relativePosition.X + size.X - bounds.X;
        if (rightOverflow > 0)
        {
            size.X -= rightOverflow;
        }

        var bottomOverflow = relativePosition.Y + size.Y - bounds.Y;
        if (bottomOverflow > 0)
        {
            size.Y -= bottomOverflow;
        }

        return (relativePosition, size);
    }

    /// <summary>
    /// Sets the OS cursor for whatever's under the mouse right now: the active drag's own
    /// cursor while one is in progress (regardless of where the mouse has since wandered --
    /// e.g. a resize drag dragged inward past the border still shows the resize cursor, not
    /// whatever the mouse happens to be over), otherwise a fresh hover hit-test each frame.
    /// Only calls MouseCursorEXT.SetCursor when the cursor actually changes, to avoid a native
    /// call every single frame regardless of whether anything changed.
    /// </summary>
    private void UpdateCursor(MouseState mouseState)
    {
        var cursor = _activeInteraction.Kind switch
        {
            WindowInteractionKind.Resize => GetResizeCursor(_activeInteraction.Edges),
            WindowInteractionKind.Move => MouseCursor.SizeAll,
            _ => GetHoverCursor(new Point(mouseState.X, mouseState.Y)),
        };

        if (cursor != CurrentCursor)
        {
            MouseCursorEXT.SetCursor(cursor);
            CurrentCursor = cursor;
        }
    }

    private MouseCursor GetHoverCursor(Point position)
    {
        var interaction = TryHitTestInteraction(position);
        return interaction.Kind switch
        {
            WindowInteractionKind.Resize => GetResizeCursor(interaction.Edges),
            WindowInteractionKind.Move => MouseCursor.SizeAll,
            _ => MouseCursor.Arrow,
        };
    }

    /// <summary>Diagonal corners get the diagonal resize cursor matching that corner's axis (Top+Left/Bottom+Right = NW-SE, Top+Right/Bottom+Left = NE-SW); a single edge gets the matching straight cursor.</summary>
    private static MouseCursor GetResizeCursor(ResizeEdges edges)
    {
        if (edges == (ResizeEdges.Top | ResizeEdges.Left) || edges == (ResizeEdges.Bottom | ResizeEdges.Right))
        {
            return MouseCursor.SizeNWSE;
        }
        if (edges == (ResizeEdges.Top | ResizeEdges.Right) || edges == (ResizeEdges.Bottom | ResizeEdges.Left))
        {
            return MouseCursor.SizeNESW;
        }
        if (edges.HasFlag(ResizeEdges.Top) || edges.HasFlag(ResizeEdges.Bottom))
        {
            return MouseCursor.SizeNS;
        }
        if (edges.HasFlag(ResizeEdges.Left) || edges.HasFlag(ResizeEdges.Right))
        {
            return MouseCursor.SizeWE;
        }

        return MouseCursor.Arrow;
    }

    private bool IsKeyPressed(KeyboardState current, Keys key) => current.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key);

    private void CycleZoom(int direction)
    {
        var zoomLevels = Enum.GetValues<ZoomLevel>();
        var currentIndex = Array.IndexOf(zoomLevels, _currentZoomLevel);
        var newIndex = MathUtility.ClampInt(currentIndex + direction, 0, zoomLevels.Length - 1);
        _currentZoomLevel = zoomLevels[newIndex];
        mapWindow.UpdateZoomLevel(_currentZoomLevel);
    }
}
