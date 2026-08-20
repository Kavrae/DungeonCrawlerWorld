using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Presentation.UI;

/// <summary>
/// What's drawn inside a window's content area, hosted via Window.SetContent instead of by
/// subclassing Window and overriding DrawContent. DebugWindowContent, SelectionWindowContent,
/// and NotificationCenter's summary window are built against this; MapWindow and TextWindow
/// instead subclass Window and override DrawContent directly, since their rendering is
/// tightly coupled to their own state and gains nothing from the extra indirection.
/// </summary>
public interface IElementContent
{
    /// <summary>
    /// Called once, after the host window's size/content area is known (so content can size
    /// itself or add child windows against ContentSize) but before Window's Opened event fires.
    /// </summary>
    void Initialize(Window hostWindow);

    void Update(GameTime gameTime);

    /// <summary>Implementations needing SpriteBatch/Texture2D read them off the host window passed to Initialize -- hostWindow.ElementPoolService.SpriteBatch/UnitRectangle (see ElementPoolService's own doc comment for why they're sourced this way instead of taken as parameters).</summary>
    void DrawContent(GameTime gameTime);

    /// <summary>Default-implemented as a no-op so existing content types don't need to change.</summary>
    void HandleKeyPress(Keys key) { }

    /// <summary>Default-implemented as a no-op so existing content types don't need to change.</summary>
    void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState) { }

    /// <summary>Default-implemented as a no-op so existing content types don't need to change.</summary>
    void HandleTextInput(char character) { }

    /// <summary>Called when this content is swapped out of its host window (e.g. TabbedContent switching tabs) so it can tear down whatever child Elements it created. Default-implemented as a no-op so existing content types don't need to change.</summary>
    void Deactivate() { }
}