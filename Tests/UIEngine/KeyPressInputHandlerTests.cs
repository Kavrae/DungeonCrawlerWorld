using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
[DoNotParallelize]
public sealed class KeyPressInputHandlerTests
{
    private static readonly KeyboardState NoKeys = new();
    private static readonly MouseState NoMouse = new();

    [TestMethod]
    public void NoModifiersGiven_FiresWhateverIsHeld()
    {
        var presses = 0;
        var handler = new KeyPressInputHandler(Keys.F5, () => presses++);

        handler.HandleKeys(new KeyboardState(Keys.F5), NoKeys, isTextBoxFocused: false);
        handler.HandleKeys(new KeyboardState(Keys.F5, Keys.LeftControl), NoKeys, isTextBoxFocused: false);

        Assert.AreEqual(2, presses);
    }

    [TestMethod]
    public void ModifiersGiven_FiresOnlyForExactlyThose()
    {
        var plainPresses = 0;
        var controlPresses = 0;
        var plain = new KeyPressInputHandler(Keys.F5, () => plainPresses++, modifiers: KeyModifiers.None);
        var control = new KeyPressInputHandler(Keys.F5, () => controlPresses++, modifiers: KeyModifiers.Control);

        foreach (var pressed in new[] { new KeyboardState(Keys.F5), new KeyboardState(Keys.F5, Keys.RightControl), new KeyboardState(Keys.F5, Keys.LeftControl, Keys.LeftShift) })
        {
            plain.HandleKeys(pressed, NoKeys, isTextBoxFocused: false);
            control.HandleKeys(pressed, NoKeys, isTextBoxFocused: false);
        }

        Assert.AreEqual(1, plainPresses);
        Assert.AreEqual(1, controlPresses);
    }

    [TestMethod]
    public void UiInputController_AHandlerAddedAndRemovedAtRuntime_HearsKeysOnlyInBetween()
    {
        var presses = 0;
        var handler = new KeyPressInputHandler(Keys.F5, () => presses++);
        var controller = new UiInputController(new UiLayerStack(), new Vector2(800, 600), new PointerState(), [], []);

        controller.Update(new KeyboardState(Keys.F5), NoMouse);
        controller.Update(NoKeys, NoMouse);
        controller.AddInputHandler(handler);
        controller.Update(new KeyboardState(Keys.F5), NoMouse);
        controller.Update(NoKeys, NoMouse);
        Assert.IsTrue(controller.RemoveInputHandler(handler));
        controller.Update(new KeyboardState(Keys.F5), NoMouse);

        Assert.AreEqual(1, presses);
        Assert.IsFalse(controller.RemoveInputHandler(handler));
    }
}
