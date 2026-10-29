using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Input;
using UIEngine.UI;

namespace Tests.UIEngine;

/// <summary>
/// Clicks, right taps and drops go to the element under the pointer and up its ancestors until one handles them
/// (PLAN-unified-pointer-picking.md, Phase 3) -- driven through a real UiInputController with no game handlers.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PointerEventRoutingTests
{
    private static readonly KeyboardState NoKeys = new();

    private static MouseState Mouse(Point position, ButtonState leftButton = ButtonState.Released, ButtonState rightButton = ButtonState.Released) =>
        new(position.X, position.Y, 0, leftButton, ButtonState.Released, rightButton, ButtonState.Released, ButtonState.Released);

    private sealed class RecordingDropHandler : IContentDropHandler
    {
        public object? DroppedOn { get; private set; }

        public bool TryDrop(object payload, object? dropTarget, Point position)
        {
            DroppedOn = dropTarget;
            return true;
        }
    }

    private sealed class FixedDropTarget(object target) : IContentDropTarget
    {
        public object? DropTargetFor(object payload, Point position) => target;
    }

    private sealed class DragSourceWindow(ElementPoolService pool) : Window(TestFonts.Shared, pool, new global::UIEngine.Rendering.UiRenderService()), IContentDragSource
    {
        public bool TryBeginContentDrag(Point pressPosition, out ContentDragStart start)
        {
            start = new ContentDragStart("payload", new Vector2(10, 10));
            return true;
        }
    }

    private static (Window Parent, Window Child) ParentWithChild(ElementPoolService pool, Vector2 parentPosition)
    {
        var parent = pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = parentPosition, Size = new Vector2(300, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        parent.Initialize();
        var child = pool.CreateElement<Window>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(50, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        parent.AddChild(child);
        return (parent, child);
    }

    private static UiInputController Controller(IContentDropHandler[] dropHandlers, params Element[] dynamicHudElements)
    {
        var layers = new UiLayerStack();
        foreach (var element in dynamicHudElements)
        {
            layers.Add(UiLayer.DynamicHud, element);
        }

        return new UiInputController(layers, new Vector2(2000, 2000), new PointerState(), [], dropHandlers);
    }

    private static void Click(UiInputController controller, Point position)
    {
        controller.Update(NoKeys, Mouse(position));
        controller.Update(NoKeys, Mouse(position, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(position));
    }

    private static void RightTap(UiInputController controller, Point position)
    {
        controller.Update(NoKeys, Mouse(position));
        controller.Update(NoKeys, Mouse(position, rightButton: ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(position));
    }

    [TestMethod]
    public void AClickOnAChildThatDoesNothingWithIt_ReachesItsParent()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var (parent, child) = ParentWithChild(pool, Vector2.Zero);
        var parentClicks = 0;
        parent.Clicked += _ => parentClicks++;

        Click(Controller([], parent), child.Rectangle.Center);

        Assert.AreEqual(1, parentClicks);
    }

    [TestMethod]
    public void AClickOnAChildThatHandlesIt_StopsThere()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var (parent, child) = ParentWithChild(pool, Vector2.Zero);
        var parentClicks = 0;
        var childClicks = 0;
        parent.Clicked += _ => parentClicks++;
        child.Clicked += _ => childClicks++;

        Click(Controller([], parent), child.Rectangle.Center);

        Assert.AreEqual(1, childClicks);
        Assert.AreEqual(0, parentClicks);
    }

    [TestMethod]
    public void ARightTapOnAChildWithNoMenu_OpensItsParentsMenu()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var (parent, child) = ParentWithChild(pool, Vector2.Zero);
        Point? parentTappedAt = null;
        parent.OnRightClicked = position => parentTappedAt = position;

        RightTap(Controller([], parent), child.Rectangle.Center);

        Assert.AreEqual(child.Rectangle.Center, parentTappedAt);
    }

    [TestMethod]
    public void ARightTapOnAChildWithAMenu_OpensOnlyThatMenu()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        var (parent, child) = ParentWithChild(pool, Vector2.Zero);
        var parentTaps = 0;
        var childTaps = 0;
        parent.OnRightClicked = _ => parentTaps++;
        child.OnRightClicked = _ => childTaps++;

        RightTap(Controller([], parent), child.Rectangle.Center);

        Assert.AreEqual(1, childTaps);
        Assert.AreEqual(0, parentTaps);
    }

    [TestMethod]
    public void ADrop_LandsOnTheNearestDropTargetAboveWhatIsUnderThePointer()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        pool.RegisterFactory<DragSourceWindow>(() => new DragSourceWindow(pool));
        var source = pool.CreateElement<DragSourceWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = new Vector2(50, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        source.Initialize();
        var (parent, child) = ParentWithChild(pool, Vector2.Zero);
        parent.DropTarget = new FixedDropTarget("parent");
        var handler = new RecordingDropHandler();
        var controller = Controller([handler], parent, source);

        var start = source.Rectangle.Center;
        controller.Update(NoKeys, Mouse(start));
        controller.Update(NoKeys, Mouse(start, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(child.Rectangle.Center, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(child.Rectangle.Center));

        Assert.AreEqual("parent", handler.DroppedOn, "The child names no target, so its parent's answers.");

        child.DropTarget = new FixedDropTarget("child");
        controller.Update(NoKeys, Mouse(start));
        controller.Update(NoKeys, Mouse(start, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(child.Rectangle.Center, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(child.Rectangle.Center));

        Assert.AreEqual("child", handler.DroppedOn);
    }

    private sealed class RefusingDropHandler : IContentDropHandler
    {
        public bool IsDropRefusedAt(object payload, object? dropTarget, Point position) => true;

        public bool TryDrop(object payload, object? dropTarget, Point position) => false;
    }

    [TestMethod]
    public void ADropAnyHandlerRefuses_IsOfferedToNone()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        pool.RegisterFactory<DragSourceWindow>(() => new DragSourceWindow(pool));
        var source = pool.CreateElement<DragSourceWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = new Vector2(50, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        source.Initialize();
        var (parent, _) = ParentWithChild(pool, Vector2.Zero);
        parent.DropTarget = new FixedDropTarget("parent");
        var recording = new RecordingDropHandler();
        var controller = Controller([recording, new RefusingDropHandler()], parent, source);

        var start = source.Rectangle.Center;
        controller.Update(NoKeys, Mouse(start));
        controller.Update(NoKeys, Mouse(start, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(parent.Rectangle.Center, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(parent.Rectangle.Center));

        Assert.IsNull(recording.DroppedOn);
    }

    [TestMethod]
    public void InMenuMode_ADropReachesATargetBehindTheMenus()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        pool.RegisterFactory<DragSourceWindow>(() => new DragSourceWindow(pool));
        var source = pool.CreateElement<DragSourceWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = new Vector2(50, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        source.Initialize();
        var (behind, _) = ParentWithChild(pool, Vector2.Zero);
        behind.DropTarget = new FixedDropTarget("behind");
        var handler = new RecordingDropHandler();
        var layers = new UiLayerStack();
        layers.Add(UiLayer.Base, behind);
        layers.Add(UiLayer.DynamicHud, source);
        layers.OpenMenuWindow(source);
        var controller = new UiInputController(layers, new Vector2(2000, 2000), new PointerState(), [], [handler]);

        var start = source.Rectangle.Center;
        controller.Update(NoKeys, Mouse(start));
        controller.Update(NoKeys, Mouse(start, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(behind.Rectangle.Center, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(behind.Rectangle.Center));

        Assert.AreEqual("behind", handler.DroppedOn);
    }

    [TestMethod]
    public void ADropWhereNothingNamesATarget_ReachesTheHandlersWithNone()
    {
        var pool = TestElementPoolServiceFactory.Create(TestFonts.Shared);
        pool.RegisterFactory<DragSourceWindow>(() => new DragSourceWindow(pool));
        var source = pool.CreateElement<DragSourceWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = new Vector2(50, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        source.Initialize();
        var handler = new RecordingDropHandler();
        var controller = Controller([handler], source);

        var start = source.Rectangle.Center;
        controller.Update(NoKeys, Mouse(start));
        controller.Update(NoKeys, Mouse(start, ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(new Point(1500, 1500), ButtonState.Pressed));
        controller.Update(NoKeys, Mouse(new Point(1500, 1500)));

        Assert.IsNull(handler.DroppedOn);
    }
}
