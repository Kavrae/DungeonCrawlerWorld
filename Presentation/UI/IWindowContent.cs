using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Presentation.UI;

/// <summary>
/// What's drawn inside a window's content area, hosted via Window.SetContent instead of by
/// subclassing Window and overriding DrawContent. DebugWindowContent, SelectionWindowContent,
/// and NotificationCenter's summary window are built against this; MapWindow and TextWindow
/// instead subclass Window and override DrawContent directly, since their rendering is
/// tightly coupled to their own state and gains nothing from the extra indirection.
/// </summary>
public interface IWindowContent
{
    /// <summary>
    /// Called once, after the host window's size/content area is known (so content can size
    /// itself or add child windows against ContentSize) but before Window's Opened event fires.
    /// </summary>
    void Initialize(Window hostWindow);

    void Update(GameTime gameTime);

    void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle);

    /// <summary>Default-implemented as a no-op so existing content types don't need to change.</summary>
    void HandleKeyPress(Keys key) { }

    /// <summary>Default-implemented as a no-op so existing content types don't need to change.</summary>
    void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState) { }
}
