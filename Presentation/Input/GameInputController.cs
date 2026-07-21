using Engine.Math;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.UI;
using Presentation.UI.Notifications;

namespace Presentation.Input;

/// <summary>
/// Translates raw keyboard/mouse state into the app's UI-level interactions: camera scroll/zoom
/// on the map window, click routing (notification popups get first claim, then root windows,
/// matching NotificationCenter's own "floats above the rest of the UI" behavior), and the
/// pause hotkey. Only tracks that the pause toggle was pressed -- what pausing actually does to
/// the simulation is GameSession's call, made by reading IsPaused each frame.
/// </summary>
public sealed class GameInputController(MapWindow mapWindow, NotificationCenter notificationCenter, IReadOnlyList<Window> rootWindows)
{
    private KeyboardState _previousKeyboardState;
    private MouseState _previousMouseState;
    private ZoomLevel _currentZoomLevel = ZoomLevel.Team;

    public bool IsPaused { get; private set; }

    public void Update(GameTime gameTime)
    {
        var keyboardState = Keyboard.GetState();

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

        var mouseState = Mouse.GetState();
        if (mouseState.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released)
        {
            var clickPosition = new Point(mouseState.X, mouseState.Y);

            // Notification popups float above the rest of the UI, so they get first claim on a click.
            if (!notificationCenter.HandleClick(clickPosition))
            {
                foreach (var window in rootWindows)
                {
                    if (window.WindowRectangle.Contains(clickPosition))
                    {
                        window.HandleClick(clickPosition);
                        break;
                    }
                }
            }
        }

        _previousKeyboardState = keyboardState;
        _previousMouseState = mouseState;
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
