using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class PointerStateTests
{
    [TestMethod]
    public void UiInputControllerUpdate_PublishesTheCursorAndAnIdleDrag()
    {
        var pointerState = new PointerState();
        var controller = TestUiInputController.Create(
            new UiLayerStack(),
            new Vector2(2000, 2000),
            BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10)),
            TestPlayerQuery.NoPlayer,
            new EventBus(),
            new ItemCatalog(),
            pointerState: pointerState);

        controller.Update(new KeyboardState(), new MouseState(12, 34, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));

        Assert.AreEqual(new Point(12, 34), pointerState.CursorPosition);
        Assert.AreEqual(new Point(12, 34), pointerState.ContentDrag.CursorPosition);
        Assert.IsFalse(pointerState.ContentDrag.Visible);
    }
}
