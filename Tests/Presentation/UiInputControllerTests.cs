using Engine.ECS.Components;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Inventory;

namespace Tests.Presentation;

/// <summary>
/// Covers UiInputController's mouse press/release tracking (Window Chrome Phase A0) and its
/// unified hit-test/raise-to-front/interaction-state-machine (Phase A1), via its internal
/// Update(KeyboardState, MouseState) overload -- the seam that lets a test drive synthetic
/// input without a real Keyboard/Mouse device (see InternalsVisibleTo in Presentation.csproj).
/// UiInputController had zero test coverage before Phase A0.
/// </summary>
[TestClass]
public sealed class UiInputControllerTests
{
    private static readonly KeyboardState NoKeys = new();

    /// <summary>Generous enough that ordinary press/drag tests never hit the screen-bounds clamp -- that clamp gets its own dedicated tests, with a deliberately small screen size.</summary>
    private static readonly Vector2 LargeScreenSize = new(2000, 2000);

    private static MouseState MouseAt(int x, int y, ButtonState leftButton) =>
        new(x, y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static MouseState MouseAtWithScroll(int x, int y, int scrollWheelValue) =>
        new(x, y, scrollWheelValue, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static MouseState MouseAtWithRightButton(int x, int y, ButtonState rightButton) =>
        new(x, y, 0, ButtonState.Released, ButtonState.Released, rightButton, ButtonState.Released, ButtonState.Released);

    private static ElementPoolService CreateWindowService() => TestElementPoolServiceFactory.Create(new FontService("Fonts"), new GlyphRenderer());

    /// <summary>
    /// Test-only convenience matching UiInputController's old four-list constructor shape --
    /// builds a fresh UiLayerStack from the four tier lists (copying each element in, not
    /// wrapping the lists themselves) since production code now only ever constructs
    /// UiInputController from a real UiLayerStack (see ShellBootstrapper). Kept here, not as
    /// a shim on UiLayerStack/UiInputController themselves, so test convenience shapes this test
    /// file, not production API surface.
    /// </summary>
    private static UiInputController CreateController(
        IReadOnlyList<Element> baseElements, IReadOnlyList<Element> staticHudElements, IReadOnlyList<Element> dynamicHudElements, IReadOnlyList<Element> userElements,
        Vector2 screenSize, HotbarController? hotbarController = null, ComponentManager? componentManager = null, IPlayerQuery? playerQuery = null)
    {
        var layers = new UiLayerStack();
        foreach (var element in baseElements)
        {
            layers.Add(UiLayer.Base, element);
        }
        foreach (var element in staticHudElements)
        {
            layers.Add(UiLayer.StaticHud, element);
        }
        foreach (var element in dynamicHudElements)
        {
            layers.Add(UiLayer.DynamicHud, element);
        }
        foreach (var element in userElements)
        {
            layers.Add(UiLayer.User, element);
        }
        return new UiInputController(layers, screenSize, hotbarController, componentManager, playerQuery);
    }

    /// <summary>Records HandleRightDragStart/HandleRightDrag calls, so UiInputController's right-button wiring (hit-test on press, total-delta-since-start on every held frame) can be verified end-to-end without a real MapWindow.</summary>
    private sealed class RightDragSpyWindow(FontService fontService, ElementPoolService windowService, GlyphRenderer glyphRenderer) : Window(fontService, windowService, glyphRenderer)
    {
        public int DragStartCallCount { get; private set; }
        public int DragEndCallCount { get; private set; }
        public int RightClickTapCallCount { get; private set; }
        public Point LastRightClickTapPosition { get; private set; }
        public List<Vector2> DragDeltas { get; } = [];

        protected override void OnRightDragStartAction() => DragStartCallCount++;
        protected override void OnRightDragAction(Vector2 totalPixelDeltaSinceStart) => DragDeltas.Add(totalPixelDeltaSinceStart);
        protected override void OnRightDragEndAction() => DragEndCallCount++;
        protected override void OnRightClickTapAction(Point position)
        {
            RightClickTapCallCount++;
            LastRightClickTapPosition = position;
        }
    }

    private static RightDragSpyWindow CreateRightDragSpyWindow(ElementPoolService windowService, FontService fontService, Vector2 relativePosition)
    {
        windowService.RegisterFactory<RightDragSpyWindow>(() => new RightDragSpyWindow(fontService, windowService, new GlyphRenderer()));
        var window = windowService.CreateElement<RightDragSpyWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        window.Initialize();
        return window;
    }

    private static Window CreateRootWindowWithCloseButton(ElementPoolService windowService, Vector2 relativePosition)
    {
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Test", CanUserClose = true },
        });
        window.Initialize();
        return window;
    }

    /// <summary>No CanUserClose (so no title buttons to intercept the click) -- lets a press over TitleRectangle resolve to a Move interaction instead.</summary>
    private static Window CreateMovableWindow(ElementPoolService windowService, Vector2 relativePosition)
    {
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Movable", CanUserMove = true },
        });
        window.Initialize();
        return window;
    }

    /// <summary>ShowBorder=true (GetResizeEdgesAt requires a border to grab) and an explicit MaximumSize well past the starting size, so growing it in a test isn't silently clamped back down (see BuildWindow's MaximumSize default).</summary>
    private static Window CreateResizableWindow(ElementPoolService windowService, Vector2 relativePosition, Vector2? maximumSize = null, Vector2? minimumSize = null)
    {
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = relativePosition,
                Size = new Vector2(200, 100),
                MinimumSize = minimumSize ?? Vector2.Zero,
                MaximumSize = maximumSize ?? new Vector2(600, 500),
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserResize = true },
        });
        window.Initialize();
        return window;
    }

    /// <summary>Fixed-size with a much taller content than the window (see TextWindowScrollingTests for the underlying scroll-bounds math) -- just enough overflow for mouse-wheel dispatch tests to have something to scroll.</summary>
    private static TextWindow CreateScrollableTextWindow(ElementPoolService windowService, Vector2 relativePosition)
    {
        var longText = string.Join(' ', Enumerable.Repeat("word", 200));
        var window = windowService.CreateElement<TextWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(150, 30), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Text = longText },
        });
        window.Initialize();
        return window;
    }

    /// <summary>
    /// Records every key routed via IWindowContent.HandleKeyPress and counts calls to
    /// HandleHotkeys -- used to observe UiInputController's routing (RouteKeyPressesToFocusedWindow/
    /// RouteHotkeysToFocusedWindow) without needing a real MapWindow or text-input control.
    /// </summary>
    private sealed class RecordingKeyContent : IElementContent
    {
        public List<Keys> PressedKeys { get; } = [];
        public int HotkeyCallCount { get; private set; }
        public List<char> TypedCharacters { get; } = [];

        public void Initialize(Window hostWindow)
        {
        }

        public void Update(GameTime gameTime)
        {
        }

        public void DrawContent(GameTime gameTime)
        {
        }

        public void HandleKeyPress(Keys key) => PressedKeys.Add(key);
        public void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState) => HotkeyCallCount++;
        public void HandleTextInput(char character) => TypedCharacters.Add(character);
    }

    /// <summary>A plain titled window with a RecordingKeyContent attached, for focus/key-routing tests.</summary>
    private static (Window Window, RecordingKeyContent Content) CreateFocusableWindowWithContent(ElementPoolService windowService, Vector2 relativePosition)
    {
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Test" },
        });
        var content = new RecordingKeyContent();
        window.SetContent(content);
        window.Initialize();
        return (window, content);
    }

    [TestMethod]
    public void PressingOverATitleButton_SetsPressedButton()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var closeButton = window.TitleButtons[0];
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = closeButton.Rectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreSame(closeButton, controller.PressedButton);
    }

    [TestMethod]
    public void ReleasingTheMouse_ClearsPressedButton()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var closeButton = window.TitleButtons[0];
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = closeButton.Rectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(closeButton, controller.PressedButton);

        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsNull(controller.PressedButton);
    }

    /// <summary>Window Chrome Phase B: the pressed visual (Draw() swapping Outset to Inset) is driven by Button.IsPressed, set true on press and unconditionally false on release.</summary>
    [TestMethod]
    public void PressingOverATitleButton_SetsIsPressedOnTheButton()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var closeButton = window.TitleButtons[0];
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = closeButton.Rectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.IsTrue(closeButton.IsPressed);
    }

    [TestMethod]
    public void ReleasingTheMouse_ClearsIsPressedOnTheButton()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var closeButton = window.TitleButtons[0];
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = closeButton.Rectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.IsTrue(closeButton.IsPressed);

        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsFalse(closeButton.IsPressed);
    }

    [TestMethod]
    public void PressingAwayFromAnyButton_LeavesPressedButtonNull()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        // Well inside the window's content area, nowhere near its title/close button.
        var contentPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(contentPoint.X, contentPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(contentPoint.X, contentPoint.Y, ButtonState.Pressed));

        Assert.IsNull(controller.PressedButton);
    }

    /// <summary>
    /// Regression guard: click routing (e.g. Close actually closing the window) must still
    /// work once the pressed-visual and press/release tracking were added -- it now fires on
    /// release rather than press (see Update's release branch), so pressing alone must NOT
    /// close the window; only the release does.
    /// </summary>
    [TestMethod]
    public void PressingThenReleasingCloseButton_ClosesTheWindow()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var closeButton = window.TitleButtons[0];
        var closed = false;
        window.Closed += _ => closed = true;
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = closeButton.Rectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.IsFalse(closed);

        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsTrue(closed);
    }

    /// <summary>
    /// The other half of the fire-on-release fix: dragging off the button entirely before
    /// releasing must cancel the click, not fire it against whatever the release position
    /// happens to land on (here, nothing -- well outside the window).
    /// </summary>
    [TestMethod]
    public void PressingCloseButton_ThenReleasingAwayFromTheWindow_DoesNotCloseIt()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var closeButton = window.TitleButtons[0];
        var closed = false;
        window.Closed += _ => closed = true;
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = closeButton.Rectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 1000, pressPoint.Y + 1000, ButtonState.Released));

        Assert.IsFalse(closed);
    }

    /// <summary>
    /// Two root windows with non-overlapping bounds -- pressing the earlier one (index 0)
    /// must move it to the end of rootWindows, exactly like Window.RaiseToFront does for a
    /// child within a parent's own list (see UiInputController.RaiseToFront).
    /// </summary>
    [TestMethod]
    public void PressingARootWindow_RaisesItToTheEndOfRootWindows()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 400));
        var layers = new UiLayerStack();
        layers.Add(UiLayer.Base, windowA);
        layers.Add(UiLayer.Base, windowB);
        var controller = new UiInputController(layers, LargeScreenSize);

        var pressPoint = windowA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        CollectionAssert.AreEqual(new[] { windowB, windowA }, layers[UiLayer.Base].ToArray());
    }

    /// <summary>
    /// Two root windows sharing identical bounds (so their close buttons land at the same
    /// screen position) -- the hit-test must resolve to whichever is topmost (last in
    /// rootWindows), never the one drawn behind it.
    /// </summary>
    [TestMethod]
    public void PressingOverlappingRootWindows_HitsOnlyTheTopmostOne()
    {
        var windowService = CreateWindowService();
        var back = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var front = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var backClosed = false;
        var frontClosed = false;
        back.Closed += _ => backClosed = true;
        front.Closed += _ => frontClosed = true;
        var controller = CreateController([back, front], [], [], [], LargeScreenSize);

        var pressPoint = front.TitleButtons[0].Rectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsTrue(frontClosed);
        Assert.IsFalse(backClosed);
    }

    /// <summary>
    /// A base window and a DynamicHud window sharing identical bounds -- the DynamicHud tier
    /// must win the hit-test regardless of list order, mirroring notifications always floating
    /// above ordinary windows (see the plan's four-tier design).
    /// </summary>
    [TestMethod]
    public void PressingOverlappingWindows_DynamicHudWins_EvenWhenCheckedSecond()
    {
        var windowService = CreateWindowService();
        var baseWindow = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var dynamicHudWindow = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var baseClosed = false;
        var dynamicHudClosed = false;
        baseWindow.Closed += _ => baseClosed = true;
        dynamicHudWindow.Closed += _ => dynamicHudClosed = true;
        var controller = CreateController([baseWindow], [], [dynamicHudWindow], [], LargeScreenSize);

        var pressPoint = dynamicHudWindow.TitleButtons[0].Rectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsTrue(dynamicHudClosed);
        Assert.IsFalse(baseClosed);
    }

    /// <summary>Pressing a movable window's title bar (away from any button) starts a Move interaction and snapshots its position/size for the drag.</summary>
    [TestMethod]
    public void PressingATitleBar_OnAMovableWindow_StartsMoveAndCapturesDragStart()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(ElementDragInteractionKind.Move, controller.ActiveInteraction.Kind);
        Assert.AreSame(window, controller.ActiveInteraction.Element);
        Assert.AreEqual(window.RelativePosition, controller.DragStartRelativePosition);
        Assert.AreEqual(window.CurrentSize, controller.DragStartSize);
    }

    /// <summary>Moving the mouse while a drag is held recomputes DragDelta -- Phase D wires Resize the same way, via SetBounds.</summary>
    [TestMethod]
    public void HoldingAMoveDrag_UpdatesDragDeltaEachFrame()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 15, pressPoint.Y + 5, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(15, 5), controller.DragDelta);
    }

    /// <summary>Window Chrome Phase C: holding a Move drag actually repositions the window, dragStartRelativePosition plus the accumulated delta, every held frame.</summary>
    [TestMethod]
    public void HoldingAMoveDrag_MovesTheWindowByTheDragDelta()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 15, pressPoint.Y + 5, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(65, 65), window.RelativePosition);
    }

    /// <summary>
    /// A window tiled by its parent (Horizontal/Vertical) has its RelativePosition recomputed
    /// on every AddChildWindow/RemoveChildWindow -- dragging it would just be fought by the
    /// next re-tile, so Window.TryHitTestInteraction (via HasFreePosition) must not even offer
    /// a Move interaction for it, regardless of CanUserMove.
    /// </summary>
    [TestMethod]
    public void PressingATitleBar_OnATiledChildWindow_DoesNotStartAMove()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        parent.Initialize();

        var child = windowService.CreateElement<Window>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Tiled", CanUserMove = true },
        });
        parent.AddChild(child);
        var controller = CreateController([parent], [], [], [], LargeScreenSize);

        var pressPoint = child.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreNotEqual(ElementDragInteractionKind.Move, controller.ActiveInteraction.Kind);
    }

    /// <summary>Releasing the mouse ends the interaction entirely, not just PressedButton -- ActiveInteraction must go back to NotHit for the next press to start clean.</summary>
    [TestMethod]
    public void ReleasingAfterAMove_ClearsActiveInteraction()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreEqual(ElementDragInteractionKind.Move, controller.ActiveInteraction.Kind);

        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.AreEqual(ElementInteraction.NotHit, controller.ActiveInteraction);
    }

    /// <summary>Window Chrome Phase D: pressing a resizable window's right border edge starts a Resize interaction flagged for that edge.</summary>
    [TestMethod]
    public void PressingARightBorderEdge_OnAResizableWindow_StartsResizeWithRightEdge()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(ElementDragInteractionKind.Resize, controller.ActiveInteraction.Kind);
        Assert.AreEqual(ResizeEdges.Right, controller.ActiveInteraction.Edges);
    }

    /// <summary>Dragging the right edge grows the window's width by the drag delta with no position change -- the left edge stays visually fixed.</summary>
    [TestMethod]
    public void HoldingARightEdgeResize_GrowsWidthWithNoPositionChange()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 40, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(240, 100), window.CurrentSize);
        Assert.AreEqual(new Vector2(50, 60), window.RelativePosition);
    }

    /// <summary>
    /// Dragging the left edge must derive the position shift from the actual size change, not
    /// the raw drag delta, so the window's right edge stays exactly where it started (the
    /// classic "resize from the left" expectation) -- relativePosition.X + size.X (the right
    /// edge, in this root window's own coordinate space) must be unchanged before and after.
    /// </summary>
    [TestMethod]
    public void HoldingALeftEdgeResize_ShrinksWidthAndKeepsTheRightEdgeFixed()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);
        var rightEdgeBeforeDrag = window.RelativePosition.X + window.CurrentSize.X;

        var pressPoint = window.BorderLeftRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 40, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(160, 100), window.CurrentSize);
        Assert.AreEqual(new Vector2(90, 60), window.RelativePosition);
        Assert.AreEqual(rightEdgeBeforeDrag, window.RelativePosition.X + window.CurrentSize.X);
    }

    /// <summary>
    /// Dragging the left edge past WindowMaximumSize must clamp the width AND keep deriving the
    /// position shift from the clamped size (not the raw delta) -- otherwise the right edge
    /// would drift once the drag exceeds the maximum, the exact bug the plan called out.
    /// </summary>
    [TestMethod]
    public void HoldingALeftEdgeResize_PastMaximumSize_ClampsWidthAndKeepsTheRightEdgeFixed()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60), maximumSize: new Vector2(250, 500));
        var controller = CreateController([window], [], [], [], LargeScreenSize);
        var rightEdgeBeforeDrag = window.RelativePosition.X + window.CurrentSize.X;

        var pressPoint = window.BorderLeftRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        // Dragging left by 100 would grow the width to 300 (200 + 100), past the 250 maximum.
        controller.Update(NoKeys, MouseAt(pressPoint.X - 100, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(250, window.CurrentSize.X);
        Assert.AreEqual(rightEdgeBeforeDrag, window.RelativePosition.X + window.CurrentSize.X);
    }

    /// <summary>A corner combines two edges in one drag -- bottom-right grows both dimensions independently.</summary>
    [TestMethod]
    public void HoldingABottomRightCornerResize_GrowsBothDimensions()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = new Point(window.Rectangle.Right - 2, window.Rectangle.Bottom - 2);
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreEqual(ResizeEdges.Bottom | ResizeEdges.Right, controller.ActiveInteraction.Edges);

        controller.Update(NoKeys, MouseAt(pressPoint.X + 30, pressPoint.Y + 20, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(230, 120), window.CurrentSize);
    }

    /// <summary>WrapContent/Fill windows compute size from content/parent, not SetSize/SetBounds -- offering a Resize interaction there would start a drag that visibly does nothing.</summary>
    [TestMethod]
    public void PressingABorderEdge_OnANonFixedResizableWindow_DoesNotStartResize()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateElement<TextWindow>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(50, 60), MaximumSize = new Vector2(400, 300), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserResize = true },
            Text = new TextOptions { Text = "Hello" },
        });
        window.Initialize();
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreNotEqual(ElementDragInteractionKind.Resize, controller.ActiveInteraction.Kind);
    }

    /// <summary>A tiled child's size is recomputed on the next AddChildWindow/RemoveChildWindow -- same HasFreePosition gate as Move, applied to Resize too.</summary>
    [TestMethod]
    public void PressingABorderEdge_OnATiledChildWindow_DoesNotStartResize()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Horizontal },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        parent.Initialize();

        var child = windowService.CreateElement<Window>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(200, 100), MaximumSize = new Vector2(600, 500), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserResize = true },
        });
        parent.AddChild(child);
        var controller = CreateController([parent], [], [], [], LargeScreenSize);

        var pressPoint = child.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreNotEqual(ElementDragInteractionKind.Resize, controller.ActiveInteraction.Kind);
    }

    /// <summary>New requirement: dragging must not move a root window off-screen -- it should stop at the screen's right/bottom edge instead of continuing to follow the mouse.</summary>
    [TestMethod]
    public void HoldingAMoveDrag_StopsAtTheScreensRightAndBottomEdges()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var smallScreen = new Vector2(300, 200);
        var controller = CreateController([window], [], [], [], smallScreen);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 1000, pressPoint.Y + 1000, ButtonState.Pressed));

        // 200x100 window on a 300x200 screen: furthest it can go is (100, 100).
        Assert.AreEqual(new Vector2(100, 100), window.RelativePosition);
    }

    /// <summary>The other side of the same requirement -- dragging toward/past the top-left must stop at (0, 0), not go negative.</summary>
    [TestMethod]
    public void HoldingAMoveDrag_StopsAtTheScreensTopAndLeftEdges()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X - 1000, pressPoint.Y - 1000, ButtonState.Pressed));

        Assert.AreEqual(Vector2.Zero, window.RelativePosition);
    }

    /// <summary>
    /// A child window must stay within its parent's content rectangle, not just the screen --
    /// the same clamp, just against a different bound (GetPositionBounds).
    /// </summary>
    [TestMethod]
    public void HoldingAMoveDrag_OnAChildWindow_StopsAtTheParentsContentEdges()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Floating },
            Layout = new ElementLayoutOptions { Size = new Vector2(300, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        parent.Initialize();
        var child = windowService.CreateElement<Window>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(50, 50), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Child", CanUserMove = true },
        });
        parent.AddChild(child);
        var controller = CreateController([parent], [], [], [], LargeScreenSize);

        var pressPoint = child.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 1000, pressPoint.Y + 1000, ButtonState.Pressed));

        Assert.AreEqual(parent.ContentSize - child.CurrentSize, child.RelativePosition);
    }

    /// <summary>
    /// Resizing from the left edge past the screen's left boundary must stop the left edge at
    /// x=0 rather than letting it go negative -- and (mirroring Phase D's own clamp-drift
    /// requirement) must shrink the size to compensate rather than just clamping position and
    /// leaving the right edge to drift.
    /// </summary>
    [TestMethod]
    public void HoldingALeftEdgeResize_StopsAtTheScreensLeftEdge()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60), maximumSize: new Vector2(1000, 1000));
        var smallScreen = new Vector2(300, 200);
        var controller = CreateController([window], [], [], [], smallScreen);
        var rightEdgeBeforeDrag = window.RelativePosition.X + window.CurrentSize.X;

        var pressPoint = window.BorderLeftRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X - 1000, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(0, window.RelativePosition.X);
        Assert.AreEqual(rightEdgeBeforeDrag, window.RelativePosition.X + window.CurrentSize.X);
    }

    /// <summary>Growing the right edge past the screen's right boundary must shrink to fit rather than pushing the window (or its right edge) off-screen.</summary>
    [TestMethod]
    public void HoldingARightEdgeResize_StopsAtTheScreensRightEdge()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60), maximumSize: new Vector2(1000, 1000));
        var smallScreen = new Vector2(300, 200);
        var controller = CreateController([window], [], [], [], smallScreen);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 1000, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(300, window.RelativePosition.X + window.CurrentSize.X);
    }

    /// <summary>
    /// New requirement: resize handles must be comfortably wider than the (often 1px) visual
    /// border -- a point a few pixels off the exact edge, which the old border-rectangle-based
    /// hit-test would have missed entirely, must still start a resize.
    /// </summary>
    [TestMethod]
    public void PressingSeveralPixelsFromTheRightEdge_StillStartsAResize()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        // 5px in from the right edge, vertically centered (well clear of the corner zones).
        var pressPoint = new Point(window.Rectangle.Right - 5, window.Rectangle.Y + (int)(window.CurrentSize.Y / 2));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(ElementDragInteractionKind.Resize, controller.ActiveInteraction.Kind);
        Assert.AreEqual(ResizeEdges.Right, controller.ActiveInteraction.Edges);
    }

    /// <summary>New requirement: hovering (no press) over a resize handle sets the matching directional OS cursor.</summary>
    [TestMethod]
    public void HoveringOverARightBorderEdge_SetsTheSizeWECursor()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var hoverPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeWE, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverATopBorderEdge_SetsTheSizeNSCursor()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var hoverPoint = window.BorderTopRectangle.Center;
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeNS, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverATopLeftCorner_SetsTheSizeNWSECursor()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var hoverPoint = new Point(window.Rectangle.X + 2, window.Rectangle.Y + 2);
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeNWSE, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverATopRightCorner_SetsTheSizeNESWCursor()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var hoverPoint = new Point(window.Rectangle.Right - 2, window.Rectangle.Y + 2);
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeNESW, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverAMovableWindowsTitleBar_SetsTheSizeAllCursor()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var hoverPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeAll, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverPlainContent_SetsTheArrowCursor()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var hoverPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.Arrow, controller.CurrentCursor);
    }

    /// <summary>
    /// While a resize drag is held, the cursor must reflect the drag in progress regardless of
    /// where the mouse has since wandered (e.g. dragged inward off the border into content) --
    /// not flicker back to Arrow just because the raw hover position no longer overlaps the
    /// handle geometry.
    /// </summary>
    [TestMethod]
    public void HoldingAResizeDrag_KeepsTheResizeCursorEvenIfTheMouseMovesOffTheHandle()
    {
        var windowService = CreateWindowService();
        // A tight MaximumSize means the right edge stops following the mouse once it's fully
        // grown -- letting the drag continue well past that point puts the mouse somewhere
        // that no longer overlaps WindowRectangle at all, which is exactly what's needed to
        // tell "always show the active drag's cursor" apart from "hover-test every frame"
        // (dragging the right edge itself, without this clamp, always leaves the border
        // sitting right under the mouse, since the edge tracks it 1:1 -- that would make the
        // two behaviors indistinguishable).
        var window = CreateResizableWindow(windowService, new Vector2(50, 60), maximumSize: new Vector2(250, 500));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreEqual(MouseCursor.SizeWE, controller.CurrentCursor);

        controller.Update(NoKeys, MouseAt(pressPoint.X + 500, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(MouseCursor.SizeWE, controller.CurrentCursor);
    }

    [TestMethod]
    public void MouseWheel_OverAScrollableWindow_ScrollsItsContent()
    {
        var windowService = CreateWindowService();
        var window = CreateScrollableTextWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var hoverPoint = window.ContentRectangle.Center;
        // -120 = one notch "down" (the FNA/XNA convention -- see UiInputController.UpdateMouseWheelScroll), which should scroll forward into the content (ScrollOffset increases), matching every other app's convention.
        controller.Update(NoKeys, MouseAtWithScroll(hoverPoint.X, hoverPoint.Y, -120));

        Assert.IsGreaterThan(0, window.ScrollOffset.Y);
    }

    /// <summary>Regression guard: hovering a window that never opted into scrolling (CanUserScrollVertical/Horizontal both false) must not scroll it, even with an active wheel delta.</summary>
    [TestMethod]
    public void MouseWheel_OverANonScrollableWindow_DoesNothing()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var hoverPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithScroll(hoverPoint.X, hoverPoint.Y, -120));

        Assert.AreEqual(Vector2.Zero, window.ScrollOffset);
    }

    /// <summary>
    /// The reported bug: hovering a non-scrollable child inside a scrollable parent (e.g. an
    /// inspector's per-component box inside its scrollable inspection container) must scroll the
    /// parent, not silently do nothing -- see UpdateMouseWheelScroll's ancestor walk-up.
    /// </summary>
    [TestMethod]
    public void MouseWheel_OverANonScrollableChildOfAScrollableParent_ScrollsTheParentInstead()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(50, 60), Size = new Vector2(300, 40), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserScrollVertical = true },
        });
        parent.Initialize();

        for (var index = 0; index < 3; index++)
        {
            var child = windowService.CreateElement<TextWindow>(parent, new ElementOptions
            {
                Layout = new ElementLayoutOptions { MaximumSize = new Vector2(parent.ContentSize.X, 1000), DisplayMode = ElementDisplayMode.WrapContent },
                Text = new TextOptions { Text = $"Child {index}" },
            });
            parent.AddChild(child);
        }

        Assert.IsGreaterThan(0, parent.MaxScrollOffset.Y, "Sanity check: the parent must actually have room to scroll.");
        var firstChild = (TextWindow)parent.ChildElements[0];
        Assert.IsFalse(firstChild.CanUserScrollVertical || firstChild.CanUserScrollHorizontal, "Sanity check: the child itself must not be scrollable -- that's the whole point of this test.");

        var controller = CreateController([parent], [], [], [], LargeScreenSize);
        var hoverPoint = firstChild.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithScroll(hoverPoint.X, hoverPoint.Y, -120));

        Assert.IsGreaterThan(0, parent.ScrollOffset.Y);
        Assert.AreEqual(Vector2.Zero, firstChild.ScrollOffset);
    }

    /// <summary>
    /// Right-press hit-tests (like a left-click) to find the drag's target and fires
    /// HandleRightDragStart on it exactly once; every held frame afterward reports the total
    /// pixel delta since the drag started (not a per-frame increment) via HandleRightDrag.
    /// This is the actual UiInputController-to-Window wiring a real MapWindow depends on for
    /// camera panning -- MapWindowTests only covers OnRightDragAction's own math in isolation.
    /// </summary>
    [TestMethod]
    public void RightMouseDrag_ReportsTotalDeltaSinceStart_ToTheWindowUnderTheCursor()
    {
        var fontService = new FontService("Fonts");
        var windowService = TestElementPoolServiceFactory.Create(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(1, window.DragStartCallCount);
        Assert.HasCount(0, window.DragDeltas, "HandleRightDragStart carries no delta -- only fires once the mouse actually moves.");

        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 10, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 25, pressPoint.Y, ButtonState.Pressed));

        CollectionAssert.AreEqual(new[] { new Vector2(-10, 0), new Vector2(-25, 0) }, window.DragDeltas,
            "Each call reports the total delta since the drag started, not this frame's increment.");
    }

    /// <summary>Releasing must end the drag -- a fresh press afterward starts a new one, anchored at wherever it begins, not the previous drag's start position.</summary>
    [TestMethod]
    public void RightMouseDrag_ReleasingThenPressingAgain_StartsAFreshDrag()
    {
        var fontService = new FontService("Fonts");
        var windowService = TestElementPoolServiceFactory.Create(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 40, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 40, pressPoint.Y, ButtonState.Released));

        // The press transition itself only fires HandleRightDragStart (mirroring the
        // left-button pattern) -- a held frame afterward is what reports a delta.
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X + 5, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X + 15, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(2, window.DragStartCallCount);
        Assert.AreEqual(new Vector2(10, 0), window.DragDeltas[^1], "The new drag's delta must be measured from its own start position (pressPoint.X + 5), not the previous drag's.");
    }

    /// <summary>Releasing must fire HandleRightDragEnd exactly once on the window the drag started over -- MapWindow uses this to settle its smooth-scroll offset onto the tile grid.</summary>
    [TestMethod]
    public void RightMouseDrag_Releasing_FiresDragEndOnce()
    {
        var fontService = new FontService("Fonts");
        var windowService = TestElementPoolServiceFactory.Create(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 10, pressPoint.Y, ButtonState.Pressed));
        Assert.AreEqual(0, window.DragEndCallCount, "Must not fire while the drag is still held.");

        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 10, pressPoint.Y, ButtonState.Released));

        Assert.AreEqual(1, window.DragEndCallCount);
    }

    /// <summary>A release with no movement at all (press and release at the same point, no held frame in between) is a tap -- HandleRightClickTap fires instead of HandleRightDragEnd, since this gesture never panned anything.</summary>
    [TestMethod]
    public void RightMouseClick_NoMovement_FiresRightClickTapInsteadOfDragEnd()
    {
        var fontService = new FontService("Fonts");
        var windowService = TestElementPoolServiceFactory.Create(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.AreEqual(1, window.RightClickTapCallCount);
        Assert.AreEqual(0, window.DragEndCallCount, "A tap must not also fire the drag-end hook.");
    }

    [TestMethod]
    public void RightClickTextBoxThenClickContextMenuOption_InvokesTheOption()
    {
        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);
        var layers = new UiLayerStack();

        var contextMenuController = new ContextMenuController(windowService);
        contextMenuController.Initialize(layers);

        windowService.RegisterFactory<TextBox>(() => new TextBox(fontService, windowService, glyphRenderer, null, contextMenuController));
        var textBox = windowService.CreateElement<TextBox>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(50, 50), Size = new Vector2(200, 30), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
        });
        textBox.Initialize();
        layers.Add(UiLayer.DynamicHud, textBox);

        var controller = new UiInputController(layers, LargeScreenSize, contextMenuController: contextMenuController);

        var textBoxCenter = textBox.Rectangle.Center;
        controller.Update(NoKeys, MouseAt(textBoxCenter.X, textBoxCenter.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(textBoxCenter.X, textBoxCenter.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(textBoxCenter.X, textBoxCenter.Y, ButtonState.Released));

        // Selection made after focusing (a left-click, like MoveCaretTo generally, always
        // collapses any existing selection) -- matches the real order of events.
        textBox.HandleTextInput('h');
        textBox.HandleTextInput('i');
        textBox.HandleHotkeys(new KeyboardState(Keys.LeftControl, Keys.A), new KeyboardState());

        controller.Update(NoKeys, MouseAtWithRightButton(textBoxCenter.X, textBoxCenter.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(textBoxCenter.X, textBoxCenter.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(textBoxCenter.X, textBoxCenter.Y, ButtonState.Released));

        Assert.IsTrue(contextMenuController.IsOpen, "Right-click-tap over a focused TextBox should open its Cut/Copy/Paste/Select All menu.");

        var cutButton = (Button)contextMenuController.Menu.ChildElements[0];
        Assert.IsTrue(cutButton.Enabled, "Cut -- selection was made via Ctrl+A above.");
        var buttonCenter = cutButton.Rectangle.Center;

        controller.Update(NoKeys, MouseAt(buttonCenter.X, buttonCenter.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(buttonCenter.X, buttonCenter.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(buttonCenter.X, buttonCenter.Y, ButtonState.Released));

        Assert.AreEqual(string.Empty, textBox.OriginalText, "Cut should have removed the selected \"hi\".");
        Assert.IsFalse(contextMenuController.IsOpen, "Selecting an option should close the menu.");
    }

    /// <summary>Same as above, but the TextBox lives inside an open menu window (e.g. the Inventory folder's own search box) -- the exact scenario TODO.md's Context menu entry calls out as the second consumer.</summary>
    [TestMethod]
    public void RightClickTextBoxInsideMenuWindowThenClickContextMenuOption_InvokesTheOption()
    {
        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);
        var layers = new UiLayerStack();

        var contextMenuController = new ContextMenuController(windowService);
        contextMenuController.Initialize(layers);

        var parentWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(400, 300), DisplayMode = ElementDisplayMode.Fixed },
        });
        parentWindow.Initialize();
        layers.Add(UiLayer.DynamicHud, parentWindow);
        layers.OpenMenuWindow(parentWindow); // Simulates Inventory's own "same Menu Mode" wiring.

        windowService.RegisterFactory<TextBox>(() => new TextBox(fontService, windowService, glyphRenderer, null, contextMenuController));
        var textBox = windowService.CreateElement<TextBox>(parentWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(50, 50), Size = new Vector2(200, 30), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
        });
        parentWindow.AddChild(textBox);

        var controller = new UiInputController(layers, LargeScreenSize, contextMenuController: contextMenuController);

        var textBoxCenter = textBox.Rectangle.Center;
        controller.Update(NoKeys, MouseAt(textBoxCenter.X, textBoxCenter.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(textBoxCenter.X, textBoxCenter.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(textBoxCenter.X, textBoxCenter.Y, ButtonState.Released));

        textBox.HandleTextInput('h');
        textBox.HandleTextInput('i');
        textBox.HandleHotkeys(new KeyboardState(Keys.LeftControl, Keys.A), new KeyboardState());

        controller.Update(NoKeys, MouseAtWithRightButton(textBoxCenter.X, textBoxCenter.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(textBoxCenter.X, textBoxCenter.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(textBoxCenter.X, textBoxCenter.Y, ButtonState.Released));

        Assert.IsTrue(contextMenuController.IsOpen, "Right-click-tap over a focused TextBox inside an open menu window should still open its context menu.");

        var cutButton = (Button)contextMenuController.Menu.ChildElements[0];
        Assert.IsTrue(cutButton.Enabled);
        var buttonCenter = cutButton.Rectangle.Center;

        controller.Update(NoKeys, MouseAt(buttonCenter.X, buttonCenter.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(buttonCenter.X, buttonCenter.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(buttonCenter.X, buttonCenter.Y, ButtonState.Released));

        Assert.AreEqual(string.Empty, textBox.OriginalText, "Cut should have removed the selected \"hi\" even with menu mode active.");
    }

    /// <summary>A release after only jitter-sized movement (below the tap-vs-drag pixel threshold) still reads as a tap, not a drag -- ordinary click imprecision shouldn't cancel an armed ability's own drag-pan behavior, nor should it be mistaken for an intentional pan.</summary>
    [TestMethod]
    public void RightMouseClick_MovementBelowTapThreshold_StillFiresRightClickTap()
    {
        var fontService = new FontService("Fonts");
        var windowService = TestElementPoolServiceFactory.Create(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X + 2, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X + 2, pressPoint.Y, ButtonState.Released));

        Assert.AreEqual(1, window.RightClickTapCallCount);
        Assert.AreEqual(0, window.DragEndCallCount);
    }

    /// <summary>A release after movement past the tap threshold is a real drag -- HandleRightDragEnd fires, not HandleRightClickTap, so an intentional camera pan keeps behaving exactly as before this distinction existed.</summary>
    [TestMethod]
    public void RightMouseDrag_MovementPastTapThreshold_FiresDragEndNotRightClickTap()
    {
        var fontService = new FontService("Fonts");
        var windowService = TestElementPoolServiceFactory.Create(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 40, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 40, pressPoint.Y, ButtonState.Released));

        Assert.AreEqual(1, window.DragEndCallCount);
        Assert.AreEqual(0, window.RightClickTapCallCount);
    }

    /// <summary>A drag that wanders back near its start before releasing still counts as a drag, not a tap -- the threshold check latches once exceeded rather than re-measuring only the final displacement.</summary>
    [TestMethod]
    public void RightMouseDrag_WandersPastThresholdThenBackToStart_StillFiresDragEndNotRightClickTap()
    {
        var fontService = new FontService("Fonts");
        var windowService = TestElementPoolServiceFactory.Create(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 40, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.AreEqual(1, window.DragEndCallCount);
        Assert.AreEqual(0, window.RightClickTapCallCount);
    }

    /// <summary>Right-dragging over empty space (nothing hit) must not throw -- it simply has nowhere to forward to until released.</summary>
    [TestMethod]
    public void RightMouseDrag_OverEmptySpace_DoesNotThrow()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        controller.Update(NoKeys, MouseAtWithRightButton(1900, 1900, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(1900, 1900, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(1850, 1900, ButtonState.Pressed));
    }

    /// <summary>Focus + keyboard navigation: clicking a window focuses it (anchored to the same raise-to-front gesture) and unfocuses whatever held focus before.</summary>
    [TestMethod]
    public void ClickingAWindow_FocusesIt_AndUnfocusesThePreviouslyFocusedWindow()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 400));
        var controller = CreateController([windowA, windowB], [], [], [], LargeScreenSize);

        var pressPointA = windowA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointA.X, pressPointA.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointA.X, pressPointA.Y, ButtonState.Pressed));

        Assert.AreSame(windowA, controller.FocusedElement);
        Assert.IsTrue(windowA.IsFocused);
        Assert.IsFalse(windowB.IsFocused);

        var pressPointB = windowB.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointB.X, pressPointB.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointB.X, pressPointB.Y, ButtonState.Pressed));

        Assert.AreSame(windowB, controller.FocusedElement);
        Assert.IsTrue(windowB.IsFocused);
        Assert.IsFalse(windowA.IsFocused);
    }

    /// <summary>Pressing where nothing is hit (see the mouse-press branch's own Window-is-null guard) must blur whatever was previously focused -- SetFocus(null) falls back to _defaultFocusElement, unset here, so FocusedElement goes all the way to null. Confirmed bug this covers: a focused TextBox (e.g. the Inventory tab search box) stayed focused/selected forever once clicked, since nothing ever cleared it on a later click elsewhere.</summary>
    [TestMethod]
    public void ClickingEmptySpace_ClearsFocus()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(window, controller.FocusedElement);

        controller.Update(NoKeys, MouseAt(1900, 1900, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(1900, 1900, ButtonState.Pressed));

        Assert.IsNull(controller.FocusedElement);
        Assert.IsFalse(window.IsFocused);
    }

    /// <summary>
    /// Closing the focused window must clear the controller's own reference to it -- otherwise
    /// a later pooled-and-reused Window instance (see WindowService.CloseWindow) would be
    /// wrongly treated as still focused. Verified via the Closed-subscription cleanup: once
    /// cleared, focusing a second window must not throw and must become the sole focused window.
    /// </summary>
    [TestMethod]
    public void ClosingTheFocusedWindow_ClearsTheControllersFocusedWindow()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(window, controller.FocusedElement);

        window.Close();

        Assert.IsNull(controller.FocusedElement);
    }

    /// <summary>A key newly pressed with a window focused reaches that window's content, not an unfocused sibling's -- the generic routing pipeline Text Input will eventually build on.</summary>
    [TestMethod]
    public void PressingAKey_RoutesOnlyToTheFocusedWindowsContent()
    {
        var windowService = CreateWindowService();
        var (focused, focusedContent) = CreateFocusableWindowWithContent(windowService, new Vector2(0, 0));
        var (other, otherContent) = CreateFocusableWindowWithContent(windowService, new Vector2(400, 400));
        var controller = CreateController([focused, other], [], [], [], LargeScreenSize);

        var pressPoint = focused.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        controller.Update(new KeyboardState(Keys.A), MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        CollectionAssert.AreEqual(new[] { Keys.A }, focusedContent.PressedKeys);
        Assert.HasCount(0, otherContent.PressedKeys);
    }

    /// <summary>
    /// UiInputController knows nothing about what any window's hotkeys actually are -- it
    /// only routes the whole keyboard state to whichever window is focused, once per Update
    /// (see RouteHotkeysToFocusedWindow). A plain window stands in for a real MapWindow here;
    /// MapWindow's own WASD/zoom/PageUp/PageDown/Space are covered at the unit level in
    /// MapWindowTests.
    /// </summary>
    [TestMethod]
    public void HotkeysAreRoutedToTheFocusedWindow()
    {
        var windowService = CreateWindowService();
        var (focused, focusedContent) = CreateFocusableWindowWithContent(windowService, new Vector2(0, 0));
        var controller = CreateController([focused], [], [], [], LargeScreenSize);

        var pressPoint = focused.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(focused, controller.FocusedElement);

        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsGreaterThan(0, focusedContent.HotkeyCallCount);
    }

    /// <summary>
    /// Regression guard for the reported bug: before routing was generic, a window's hotkeys
    /// fired regardless of what was actually focused, so typing into a focused text window
    /// could spuriously trigger another window's controls. Now hotkeys only ever reach
    /// whichever window is actually focused.
    /// </summary>
    [TestMethod]
    public void HotkeysAreNotRoutedToAnUnfocusedWindow()
    {
        var windowService = CreateWindowService();
        var (focused, focusedContent) = CreateFocusableWindowWithContent(windowService, new Vector2(0, 0));
        var (other, otherContent) = CreateFocusableWindowWithContent(windowService, new Vector2(400, 400));
        var controller = CreateController([focused, other], [], [], [], LargeScreenSize);

        var pressPoint = focused.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(focused, controller.FocusedElement);

        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsGreaterThan(0, focusedContent.HotkeyCallCount);
        Assert.AreEqual(0, otherContent.HotkeyCallCount);
    }

    /// <summary>Nothing focused yet (e.g. before the composition root's initial FocusWindow call) means no window's hotkeys fire -- there's no "default owner" once focus is a real concept.</summary>
    [TestMethod]
    public void HotkeysAreNotRoutedWhenNothingIsFocused()
    {
        var windowService = CreateWindowService();
        var (window, content) = CreateFocusableWindowWithContent(windowService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));

        Assert.AreEqual(0, content.HotkeyCallCount);
    }

    /// <summary>
    /// A typed character (simulated via OnTextInput -- the internal seam a real
    /// TextInputEXT.TextInput subscription feeds in production, see the UiInputController
    /// constructor) reaches only the focused window's content, mirroring HandleKeyPress/
    /// HandleHotkeys routing.
    /// </summary>
    [TestMethod]
    public void TypedCharacters_RouteOnlyToTheFocusedWindowsContent()
    {
        var windowService = CreateWindowService();
        var (focused, focusedContent) = CreateFocusableWindowWithContent(windowService, new Vector2(0, 0));
        var (other, otherContent) = CreateFocusableWindowWithContent(windowService, new Vector2(400, 400));
        var controller = CreateController([focused, other], [], [], [], LargeScreenSize);

        var pressPoint = focused.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        controller.OnTextInput('a');
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        CollectionAssert.AreEqual(new[] { 'a' }, focusedContent.TypedCharacters);
        Assert.HasCount(0, otherContent.TypedCharacters);
    }

    /// <summary>Characters typed before anything is focused are buffered, not lost or misrouted -- once a window is focused, the next Update drains whatever had accumulated.</summary>
    [TestMethod]
    public void TypedCharacters_BufferedBeforeAnyUpdate_AreNotLostOnceFocused()
    {
        var windowService = CreateWindowService();
        var (focused, focusedContent) = CreateFocusableWindowWithContent(windowService, new Vector2(0, 0));
        var controller = CreateController([focused], [], [], [], LargeScreenSize);
        controller.FocusElement(focused);

        controller.OnTextInput('h');
        controller.OnTextInput('i');
        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));

        CollectionAssert.AreEqual(new[] { 'h', 'i' }, focusedContent.TypedCharacters);
    }

    /// <summary>
    /// A window with a focusable TextBox child is never itself the terminal focus target for a
    /// plain (non-drag) click -- focusing it redirects into its first TextBox instead, per
    /// UiInputController.SetFocus's NextFocusableDescendant redirect.
    /// </summary>
    [TestMethod]
    public void ClickingAWindowWithATextBoxChild_FocusesTheTextBoxInstead()
    {
        var windowService = CreateWindowService();
        var container = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Form", CanUserMove = true },
        });
        container.Initialize();
        var textBox = windowService.CreateElement<TextBox>(container, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(180, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        container.AddChild(textBox);
        var controller = CreateController([container], [], [], [], LargeScreenSize);

        // Click the container's own content area, below/outside the TextBox child's own
        // Rectangle (180x50 at the top) -- content-agnostic (falls through to
        // ElementInteraction.Click(container), Kind.None), so it can't be mistaken for directly
        // clicking the TextBox child itself, nor for a Move/Resize drag -- see the next test for
        // why Move specifically no longer triggers this redirect.
        var contentPoint = new Point((int)container.ContentRectangle.Right - 5, (int)container.ContentRectangle.Bottom - 5);
        controller.Update(NoKeys, MouseAt(contentPoint.X, contentPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(contentPoint.X, contentPoint.Y, ButtonState.Pressed));

        Assert.AreSame(textBox, controller.FocusedElement);
        Assert.IsTrue(textBox.IsFocused);
        Assert.IsFalse(container.IsFocused);
    }

    /// <summary>
    /// Dragging a window by its title bar (a Move interaction, not a plain click) deliberately
    /// does NOT redirect focus into a TextBox child -- withdrawn from HandleMousePress's focus
    /// resolution in favor of a real, explicit control-selection feature later (see the medium
    /// priority Presentation TODO on a comprehensive control-selection feature). Confirmed bug
    /// this covers: dragging the Inventory window by its title bar silently stole keyboard focus
    /// into its search box, the same way a resize drag did (see the sibling Resize-focused test
    /// coverage this mirrors).
    /// </summary>
    [TestMethod]
    public void DraggingAWindowsTitleBar_DoesNotFocusItsTextBoxChild()
    {
        var windowService = CreateWindowService();
        var container = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Form", CanUserMove = true },
        });
        container.Initialize();
        var textBox = windowService.CreateElement<TextBox>(container, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(180, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        container.AddChild(textBox);
        var controller = CreateController([container], [], [], [], LargeScreenSize);

        var titlePoint = container.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(titlePoint.X, titlePoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(titlePoint.X, titlePoint.Y, ButtonState.Pressed));

        Assert.IsNull(controller.FocusedElement);
        Assert.IsFalse(textBox.IsFocused);
        Assert.IsFalse(container.IsFocused);
    }

    /// <summary>Enter on a TextBox with a sibling TextBox asks UiInputController (via FocusRequested) to move focus to it -- the same mechanism a click or Tab would use.</summary>
    [TestMethod]
    public void SubmittingATextBox_MovesFocusToTheNextTextBoxSibling()
    {
        var windowService = CreateWindowService();
        var container = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        container.Initialize();
        var firstTextBox = windowService.CreateElement<TextBox>(container, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(180, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        container.AddChild(firstTextBox);
        var secondTextBox = windowService.CreateElement<TextBox>(container, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 60), Size = new Vector2(180, 50), DisplayMode = ElementDisplayMode.Fixed },
        });
        container.AddChild(secondTextBox);
        var controller = CreateController([container], [], [], [], LargeScreenSize);
        controller.FocusElement(firstTextBox);

        controller.Update(new KeyboardState(Keys.Enter), MouseAt(0, 0, ButtonState.Released));

        Assert.AreSame(secondTextBox, controller.FocusedElement);
    }

    /// <summary>Tab advances focus to the next root window, wrapping past the last one back to the first -- rootWindows only, in list order.</summary>
    [TestMethod]
    public void PressingTab_CyclesFocusThroughRootWindows_Wrapping()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 400));
        var rootWindows = new List<Element> { windowA, windowB };
        var controller = CreateController(rootWindows, [], [], [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));
        Assert.AreSame(windowA, controller.FocusedElement);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));
        Assert.AreSame(windowB, controller.FocusedElement);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));
        Assert.AreSame(windowA, controller.FocusedElement);
    }

    /// <summary>
    /// Regression guard: Tab used to also raise the newly-focused window to front, exactly
    /// like a click would -- but reordering rootWindows on every Tab press corrupted the index
    /// this method itself relies on for the *next* press (see CycleFocus's own remarks), so
    /// Tab no longer touches z-order at all. Confirms the list stays untouched by a Tab press.
    /// </summary>
    [TestMethod]
    public void PressingTab_DoesNotReorderRootWindows()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 400));
        var layers = new UiLayerStack();
        layers.Add(UiLayer.Base, windowA);
        layers.Add(UiLayer.Base, windowB);
        var controller = new UiInputController(layers, LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));

        CollectionAssert.AreEqual(new[] { windowA, windowB }, layers[UiLayer.Base].ToArray());
    }

    /// <summary>Shift+Tab cycles the other direction.</summary>
    [TestMethod]
    public void PressingShiftTab_CyclesFocusBackward()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 400));
        var rootWindows = new List<Element> { windowA, windowB };
        var controller = CreateController(rootWindows, [], [], [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab, Keys.LeftShift), MouseAt(0, 0, ButtonState.Released));

        Assert.AreSame(windowB, controller.FocusedElement);
    }

    /// <summary>
    /// Regression test for the reported bug: with three root windows, repeated Shift+Tab used
    /// to oscillate between only two of them (the direction -1 step, combined with Tab's old
    /// raise-to-front side effect, meant the third window was never reachable again after the
    /// first press moved past it) -- Tab (direction +1) happened to visit all three by
    /// coincidence, but Shift+Tab did not. Both directions must visit all three, repeating in a
    /// stable cycle.
    /// </summary>
    [TestMethod]
    public void PressingShiftTabRepeatedly_CyclesThroughAllThreeRootWindows()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 0));
        var windowC = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 400));
        var rootWindows = new List<Element> { windowA, windowB, windowC };
        var controller = CreateController(rootWindows, [], [], [], LargeScreenSize);

        var visited = new List<Element>();
        for (var i = 0; i < 6; i++)
        {
            controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
            controller.Update(new KeyboardState(Keys.Tab, Keys.LeftShift), MouseAt(0, 0, ButtonState.Released));
            visited.Add(controller.FocusedElement!);
        }

        // Backward from unfocused starts at the last window (C), then keeps stepping backward,
        // wrapping: C, B, A, C, B, A.
        CollectionAssert.AreEqual(new[] { windowC, windowB, windowA, windowC, windowB, windowA }, visited);
    }

    /// <summary>A window with CanUserFocus = false (e.g. the debug stats window) is a concrete opt-out: it never becomes the focused element itself, but clicking it still blurs whatever *was* focused (SetFocus(null) falls back to _defaultFocusElement, unset here) -- "click away to blur" applies to any non-focusable target, not just empty space. See ClickingEmptySpace_ClearsFocus for the same behavior with no target at all.</summary>
    [TestMethod]
    public void ClickingANonFocusableWindow_ClearsFocus()
    {
        var windowService = CreateWindowService();
        var focusable = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var nonFocusable = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(400, 400), Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserFocus = false },
        });
        nonFocusable.Initialize();
        var controller = CreateController([focusable, nonFocusable], [], [], [], LargeScreenSize);

        var pressPointFocusable = focusable.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointFocusable.X, pressPointFocusable.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointFocusable.X, pressPointFocusable.Y, ButtonState.Pressed));
        Assert.AreSame(focusable, controller.FocusedElement);

        var pressPointNonFocusable = nonFocusable.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointNonFocusable.X, pressPointNonFocusable.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointNonFocusable.X, pressPointNonFocusable.Y, ButtonState.Pressed));

        Assert.IsNull(controller.FocusedElement);
        Assert.IsFalse(focusable.IsFocused);
        Assert.IsFalse(nonFocusable.IsFocused);
    }

    /// <summary>A CanUserFocus = false window (e.g. the debug stats window) is skipped entirely by Tab -- it's never a stop in the cycle.</summary>
    [TestMethod]
    public void PressingTab_SkipsNonFocusableRootWindows()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var nonFocusable = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(400, 0), Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserFocus = false },
        });
        nonFocusable.Initialize();
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 400));
        var rootWindows = new List<Element> { windowA, nonFocusable, windowB };
        var controller = CreateController(rootWindows, [], [], [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));
        Assert.AreSame(windowA, controller.FocusedElement);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));

        Assert.AreSame(windowB, controller.FocusedElement);
        Assert.IsFalse(nonFocusable.IsFocused);
    }

    private static (Window Parent, Window ChildA, Window ChildB) CreateParentWithTwoCloseableChildren(ElementPoolService windowService)
    {
        var parent = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(300, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        parent.Initialize();

        var childA = windowService.CreateElement<Window>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(300, 50), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "A", CanUserClose = true },
        });
        parent.AddChild(childA);

        var childB = windowService.CreateElement<Window>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(300, 50), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "B", CanUserClose = true },
        });
        parent.AddChild(childB);

        return (parent, childA, childB);
    }

    /// <summary>
    /// Regression test for the reported behavior: closing the focused window must not just
    /// leave focus on nothing when a genuine sibling exists -- e.g. closing the topmost active
    /// notification popup should hand focus to the next one, not clear it. Always-on-top tier
    /// siblings specifically, mirroring NotificationCenter's stack of popups.
    /// </summary>
    [TestMethod]
    public void ClosingTheFocusedAlwaysOnTopWindow_RedirectsFocusToTheRemainingSibling()
    {
        var windowService = CreateWindowService();
        var notificationA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var notificationB = CreateRootWindowWithCloseButton(windowService, new Vector2(300, 0));
        var controller = CreateController([], [], [notificationA, notificationB], [], LargeScreenSize);

        var pressPoint = notificationA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(notificationA, controller.FocusedElement);

        notificationA.Close();

        Assert.AreSame(notificationB, controller.FocusedElement);
    }

    /// <summary>A single Escape tap closes only the frontmost (last in the list -- see UiInputController's own "topmost (last-raised) first" tier-ordering doc comment) closeable DynamicHUD window, leaving the rest open.</summary>
    [TestMethod]
    public void HandleEscape_SingleTap_ClosesOnlyTheTopmostClosableDynamicHudWindow()
    {
        var windowService = CreateWindowService();
        var back = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var front = CreateRootWindowWithCloseButton(windowService, new Vector2(300, 0));
        var controller = CreateController([], [], [back, front], [], LargeScreenSize);

        controller.Update(new KeyboardState(Keys.Escape), MouseAt(0, 0, ButtonState.Released));

        Assert.IsFalse(front.IsVisible, "The topmost (last-added) window must close.");
        Assert.IsTrue(back.IsVisible, "A single tap must not touch anything else.");
    }

    /// <summary>Non-closeable DynamicHUD elements (Folder icons, the Armed Hotkey Summary -- CanUserClose false, or not even a Window) are never targeted -- Escape skips past one to find the next real closeable window underneath it.</summary>
    [TestMethod]
    public void HandleEscape_TopmostElementIsNotCloseable_SkipsItAndClosesTheNextOneDown()
    {
        var windowService = CreateWindowService();
        var closeable = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var chrome = CreateMovableWindow(windowService, new Vector2(300, 0)); // CanUserClose defaults false.
        var controller = CreateController([], [], [closeable, chrome], [], LargeScreenSize);

        controller.Update(new KeyboardState(Keys.Escape), MouseAt(0, 0, ButtonState.Released));

        Assert.IsFalse(closeable.IsVisible);
        Assert.IsTrue(chrome.IsVisible, "Non-closeable chrome must never be closed by Escape.");
    }

    /// <summary>Holding Escape past EscapeHoldCloseAllFrames closes every closeable DynamicHUD window at once, not just the topmost -- the escape hatch for a player buried under several popups at once.</summary>
    [TestMethod]
    public void HandleEscape_HeldPastThreshold_ClosesEveryClosableDynamicHudWindow()
    {
        var windowService = CreateWindowService();
        var back = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var front = CreateRootWindowWithCloseButton(windowService, new Vector2(300, 0));
        var controller = CreateController([], [], [back, front], [], LargeScreenSize);

        // 40 frames (~0.67s at 60fps) is comfortably past the 0.5s hold threshold.
        for (var frame = 0; frame < 40; frame++)
        {
            controller.Update(new KeyboardState(Keys.Escape), MouseAt(0, 0, ButtonState.Released));
        }

        Assert.IsFalse(front.IsVisible);
        Assert.IsFalse(back.IsVisible);
    }

    private static (Window Popup, Window Child) CreatePopupWithFocusableChild(ElementPoolService windowService, Vector2 relativePosition)
    {
        var popup = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(300, 200), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = "Popup", CanUserClose = true },
        });
        popup.Initialize();

        var child = windowService.CreateElement<Window>(popup, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(280, 150), DisplayMode = ElementDisplayMode.Fixed },
        });
        popup.AddChild(child);

        return (popup, child);
    }

    /// <summary>
    /// Regression test for the reported bug: closing the quest-composer popup while its
    /// TextBox child holds focus left focus stranded on the (now closed/hidden) TextBox
    /// instead of falling back to the map window -- popup.Close() only ever fires Closed on
    /// popup itself, never on the still-focused child, so a redirect wired to just the exact
    /// focused window's own Closed event never saw it happen at all. UiInputController now
    /// subscribes Closed across the focused window's whole ancestor chain (see
    /// _focusedWindowAncestorChain), not just the focused window itself.
    /// </summary>
    [TestMethod]
    public void ClosingAWindowWhoseFocusedChildIsNotItself_StillRedirectsFocusAwayFromTheChild()
    {
        var windowService = CreateWindowService();
        var (popup, child) = CreatePopupWithFocusableChild(windowService, new Vector2(0, 0));
        var mapWindowStandIn = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 0));
        var controller = CreateController([popup, mapWindowStandIn], [], [], [], LargeScreenSize);
        controller.SetDefaultFocusElement(mapWindowStandIn);

        var pressPoint = child.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(child, controller.FocusedElement);

        popup.Close();

        Assert.AreSame(mapWindowStandIn, controller.FocusedElement);
    }

    /// <summary>Same redirect, generalized to sibling child windows under a shared parent (e.g. a future multi-pane form), not just the always-on-top notification stack.</summary>
    [TestMethod]
    public void ClosingTheFocusedChildWindow_RedirectsFocusToItsSiblingChild()
    {
        var windowService = CreateWindowService();
        var (parent, childA, childB) = CreateParentWithTwoCloseableChildren(windowService);
        var controller = CreateController([parent], [], [], [], LargeScreenSize);

        var pressPoint = childA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(childA, controller.FocusedElement);

        childA.Close();

        Assert.AreSame(childB, controller.FocusedElement);
    }

    /// <summary>Same trigger, but via minimizing (WindowMinimizeRestoreBehavior's real WindowDisplayMode.Minimized toggle) instead of closing -- a minimized window reads as "no longer active" the same way a closed one does.</summary>
    [TestMethod]
    public void MinimizingTheFocusedWindow_RedirectsFocusToTheRemainingSibling()
    {
        var windowService = CreateWindowService();
        var notificationA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var notificationB = CreateRootWindowWithCloseButton(windowService, new Vector2(300, 0));
        var controller = CreateController([], [], [notificationA, notificationB], [], LargeScreenSize);

        var pressPoint = notificationA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(notificationA, controller.FocusedElement);

        notificationA.SetDisplayMode(ElementDisplayMode.Minimized);

        Assert.AreSame(notificationB, controller.FocusedElement);
    }

    /// <summary>Regression guard: DisplayModeChanged fires on every mode change, not just transitions into Minimized -- an unrelated mode change (e.g. Fixed to Fill) must not spuriously redirect focus away.</summary>
    [TestMethod]
    public void ChangingTheFocusedWindowsDisplayModeToSomethingOtherThanMinimized_DoesNotRedirectFocus()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(window, controller.FocusedElement);

        window.SetDisplayMode(ElementDisplayMode.Fill);

        Assert.AreSame(window, controller.FocusedElement);
    }

    /// <summary>
    /// Regression test for task 2: closing the quest-composer popup (a root window) must not
    /// grab some unrelated root panel (e.g. the selection window) as a substitute -- root-tier
    /// windows are fixed, distinct panels, not an interchangeable stack -- it must fall all the
    /// way through to the configured default focus window (the map window in production), same
    /// as if nothing else were open at all.
    /// </summary>
    [TestMethod]
    public void ClosingAFocusedRootWindow_FallsBackToTheDefaultFocusWindow_NotAnUnrelatedRootSibling()
    {
        var windowService = CreateWindowService();
        var popup = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var unrelatedRootWindow = CreateRootWindowWithCloseButton(windowService, new Vector2(300, 0));
        var mapWindowStandIn = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 300));
        var controller = CreateController([popup, unrelatedRootWindow, mapWindowStandIn], [], [], [], LargeScreenSize);
        controller.SetDefaultFocusElement(mapWindowStandIn);

        var pressPoint = popup.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(popup, controller.FocusedElement);

        popup.Close();

        Assert.AreSame(mapWindowStandIn, controller.FocusedElement);
    }

    /// <summary>Regression test for task 2's other half: with no default focus window configured (e.g. a test that never calls SetDefaultFocusWindow) and no eligible sibling, closing the focused window still just clears focus rather than throwing.</summary>
    [TestMethod]
    public void ClosingTheOnlyFocusedWindow_WithNoDefaultFocusWindowConfigured_LeavesFocusNull()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = CreateController([window], [], [], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(window, controller.FocusedElement);

        window.Close();

        Assert.IsNull(controller.FocusedElement);
    }

    /// <summary>
    /// Regression test mirroring NotificationCenter's summary bar: a CanUserFocus = false
    /// sibling (e.g. a click-only HUD element sharing the always-on-top tier with real
    /// notification popups) must never be picked as the "next" window on redirect -- it should
    /// be skipped just like Tab cycling already skips it, falling through to the default focus
    /// window instead.
    /// </summary>
    [TestMethod]
    public void ClosingTheFocusedWindow_SkipsANonFocusableSiblingAndFallsBackToDefault()
    {
        var windowService = CreateWindowService();
        var summaryBarStandIn = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 300), Size = new Vector2(200, 30), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserFocus = false },
        });
        summaryBarStandIn.Initialize();
        var notification = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var mapWindowStandIn = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 400));
        var controller = CreateController([], [], [summaryBarStandIn, notification], [], LargeScreenSize);
        controller.SetDefaultFocusElement(mapWindowStandIn);

        var pressPoint = notification.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(notification, controller.FocusedElement);

        notification.Close();

        Assert.AreSame(mapWindowStandIn, controller.FocusedElement);
    }

    private static TextBox CreateFocusableTextBox(ElementPoolService windowService, Vector2 relativePosition)
    {
        var textBox = windowService.CreateElement<TextBox>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 60), DisplayMode = ElementDisplayMode.Fixed },
        });
        textBox.Initialize();
        return textBox;
    }

    /// <summary>
    /// Regression test: SDL text-input mode (which gates OS IME composition popups, and on
    /// touch/mobile SDL backends the on-screen keyboard) must track TextBox focus rather than
    /// run for the whole app session -- started only once an actual TextBox gains focus, and
    /// stopped again once focus moves to anything that isn't one. Substitutes call-recording
    /// fakes for StartTextInput/StopTextInput (see UiInputController) rather than asserting
    /// on TextInputEXT.IsTextInputActive() directly -- that reads real SDL state, which isn't
    /// reliably observable with no actual SDL window backing this headless test environment.
    /// </summary>
    [TestMethod]
    public void FocusMovingToAndAwayFromATextBox_TogglesSdlTextInputMode()
    {
        var windowService = CreateWindowService();
        var textBox = CreateFocusableTextBox(windowService, new Vector2(0, 0));
        var plainWindow = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 0));
        var controller = CreateController([textBox, plainWindow], [], [], [], LargeScreenSize);
        var startCount = 0;
        var stopCount = 0;
        controller.StartTextInput = () => startCount++;
        controller.StopTextInput = () => stopCount++;

        var textBoxPressPoint = textBox.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(textBoxPressPoint.X, textBoxPressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(textBoxPressPoint.X, textBoxPressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(textBox, controller.FocusedElement);
        Assert.AreEqual(1, startCount);
        Assert.AreEqual(0, stopCount);

        var plainWindowPressPoint = plainWindow.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(plainWindowPressPoint.X, plainWindowPressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(plainWindowPressPoint.X, plainWindowPressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(plainWindow, controller.FocusedElement);
        Assert.AreEqual(1, startCount);
        Assert.AreEqual(1, stopCount);
    }

    /// <summary>Regression guard: tabbing/clicking between two ordinary (non-TextBox) windows must not toggle text-input mode at all -- only an actual TextBox &lt;-&gt; non-TextBox edge should.</summary>
    [TestMethod]
    public void FocusMovingBetweenTwoNonTextBoxWindows_NeverTogglesSdlTextInputMode()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 0));
        var controller = CreateController([windowA, windowB], [], [], [], LargeScreenSize);
        var startCount = 0;
        var stopCount = 0;
        controller.StartTextInput = () => startCount++;
        controller.StopTextInput = () => stopCount++;

        var pressPointA = windowA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointA.X, pressPointA.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointA.X, pressPointA.Y, ButtonState.Pressed));
        var pressPointB = windowB.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointB.X, pressPointB.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointB.X, pressPointB.Y, ButtonState.Pressed));

        Assert.AreEqual(0, startCount);
        Assert.AreEqual(0, stopCount);
    }

    /// <summary>
    /// Builds an InventoryItemStackCell (a drag source) and a HotbarContent-hosting Window (a
    /// drag source/drop target) sharing one ComponentManager/ItemCatalog/player entity, the
    /// pieces UiInputController's content-drag path needs -- see its own doc comment. The cell
    /// is placed far from the hotbar window so a press-then-release pair between them always
    /// exceeds ContentDragTapThresholdPixels.
    /// </summary>
    private static (InventoryItemStackCell Cell, Window HotbarWindow, HotbarContent Hotbar, ComponentManager ComponentManager, Guid ItemId) BuildDragAndDropHarness()
    {
        const int playerEntityId = 1;

        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = playerEntityId };
        var itemId = Guid.NewGuid();
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(itemId, "Test Item", null, "t", Color.White, Tags: [], Effects: []));
        var stackInstanceId = InventoryActions.AddItem(componentManager, playerEntityId, itemId, quantity: 1);

        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(
            fontService, windowService, glyphRenderer, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer()));

        var cell = windowService.CreateElement<InventoryItemStackCell>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(24, 24), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        cell.Configure(playerEntityId, itemId, stackInstanceId, null, "t", Color.White, quantity: 1, chargeText: null, isDisabled: false, isDivergent: false, mergedStackBadgeVisible: false, cellSize: new Vector2(24, 24));
        cell.Initialize();

        var hotbar = new HotbarContent(
            world, new MapViewState(), componentManager, new EventBus(), new ActionCatalog(), itemCatalog,
            fontService, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), new Vector2(1920, 1080));
        var hotbarWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = hotbar.Size, DisplayMode = ElementDisplayMode.Fixed },
        });
        hotbarWindow.SetContent(hotbar);
        hotbarWindow.Initialize();

        return (cell, hotbarWindow, hotbar, componentManager, itemId);
    }

    /// <summary>Same shape as BuildDragAndDropHarness, but the item stack (and the cell dragging it) belongs to a second, non-player entity -- for asserting that a hotbar bind is refused when the drag didn't originate from the player's own inventory.</summary>
    private static (InventoryItemStackCell Cell, Window HotbarWindow, HotbarContent Hotbar, ComponentManager ComponentManager, Game.World.World World, Guid ItemId) BuildNonPlayerOriginDragToHotbarHarness()
    {
        const int playerEntityId = 1;
        const int corpseEntityId = 2;

        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = playerEntityId };
        var itemId = Guid.NewGuid();
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(itemId, "Test Item", null, "t", Color.White, Tags: [], Effects: []));
        var stackInstanceId = InventoryActions.AddItem(componentManager, corpseEntityId, itemId, quantity: 1);

        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(
            fontService, windowService, glyphRenderer, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer()));

        var cell = windowService.CreateElement<InventoryItemStackCell>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(24, 24), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        cell.Configure(corpseEntityId, itemId, stackInstanceId, null, "t", Color.White, quantity: 1, chargeText: null, isDisabled: false, isDivergent: false, mergedStackBadgeVisible: false, cellSize: new Vector2(24, 24));
        cell.Initialize();

        var hotbar = new HotbarContent(
            world, new MapViewState(), componentManager, new EventBus(), new ActionCatalog(), itemCatalog,
            fontService, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), new Vector2(1920, 1080));
        var hotbarWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = hotbar.Size, DisplayMode = ElementDisplayMode.Fixed },
        });
        hotbarWindow.SetContent(hotbar);
        hotbarWindow.Initialize();

        return (cell, hotbarWindow, hotbar, componentManager, world, itemId);
    }

    [TestMethod]
    public void Drag_FromNonPlayerEntitysInventoryCellToHotbarSlot_DoesNotBindTheItem()
    {
        var (cell, hotbarWindow, hotbar, componentManager, world, _) = BuildNonPlayerOriginDragToHotbarHarness();
        var controller = CreateController([cell], [hotbarWindow], [], [], LargeScreenSize, componentManager: componentManager, playerQuery: world);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        var dropPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, (int)hotbarWindow.ContentAbsolutePosition.Y + (int)(hotbar.Size.Y / 2f));
        controller.Update(NoKeys, MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        Assert.IsFalse(ItemHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), world.PlayerEntityId, HotkeySlot.Base1, out _));
    }

    [TestMethod]
    public void Drag_FromNonPlayerEntitysInventoryCell_NeverTurnsOnHotbarDragHighlight()
    {
        var (cell, hotbarWindow, hotbar, componentManager, world, _) = BuildNonPlayerOriginDragToHotbarHarness();
        var controller = CreateController([cell], [hotbarWindow], [], [], LargeScreenSize, componentManager: componentManager, playerQuery: world);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.IsFalse(hotbar.IsAcceptingDrag);
    }

    [TestMethod]
    public void Drag_FromInventoryCellToHotbarSlot_BindsTheItem()
    {
        var (cell, hotbarWindow, hotbar, componentManager, itemId) = BuildDragAndDropHarness();
        var controller = CreateController([cell], [hotbarWindow], [], [], LargeScreenSize);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        // Base is vertically centered against Expansion's current height, not flush at the top --
        // the window's own vertical center always falls inside Base1's row.
        var dropPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, (int)hotbarWindow.ContentAbsolutePosition.Y + (int)(hotbar.Size.Y / 2f));
        controller.Update(NoKeys, MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        Assert.IsTrue(InventoryQueries.TryGetStack(componentManager.GetMultiPool<InventoryItemStackComponent>(), 1, itemId, out var stack));
        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), 1, HotkeySlot.Base1, out var boundStackInstanceId));
        Assert.AreEqual(stack.StackInstanceId, boundStackInstanceId);
    }

    /// <summary>Binding is a reference, not a transfer -- InventoryItemStackComponent's own Quantity must be untouched by the drag.</summary>
    [TestMethod]
    public void Drag_FromInventoryCellToHotbarSlot_DoesNotRemoveTheItemFromInventory()
    {
        var (cell, hotbarWindow, hotbar, componentManager, itemId) = BuildDragAndDropHarness();
        var controller = CreateController([cell], [hotbarWindow], [], [], LargeScreenSize);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        var dropPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, (int)hotbarWindow.ContentAbsolutePosition.Y + (int)(hotbar.Size.Y / 2f));
        controller.Update(NoKeys, MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        Assert.IsTrue(InventoryQueries.TryGetStack(componentManager.GetMultiPool<InventoryItemStackComponent>(), 1, itemId, out var stack));
        Assert.AreEqual(1, stack.Quantity);
    }

    [TestMethod]
    public void Drag_FromInventoryCellReleasedAwayFromTheHotbar_BindsNothing()
    {
        var (cell, hotbarWindow, _, componentManager, _) = BuildDragAndDropHarness();
        var controller = CreateController([cell], [hotbarWindow], [], [], LargeScreenSize);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(1000, 1000, ButtonState.Released));

        Assert.IsFalse(ItemHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), 1, HotkeySlot.Slot1, out _));
    }

    [TestMethod]
    public void Drag_FromABoundHotbarSlotToAwayFromTheHotbar_UnbindsIt()
    {
        var (_, hotbarWindow, hotbar, componentManager, itemId) = BuildDragAndDropHarness();
        hotbar.BindItem(HotkeySlot.Base1, itemId);
        var controller = CreateController([], [hotbarWindow], [], [], LargeScreenSize);

        // Base is vertically centered against Expansion's current height, not flush at the top --
        // the window's own vertical center always falls inside Base1's row.
        var pressPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, (int)hotbarWindow.ContentAbsolutePosition.Y + (int)(hotbar.Size.Y / 2f));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(1000, 1000, ButtonState.Released));

        Assert.IsFalse(ItemHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), 1, HotkeySlot.Base1, out _));
    }

    /// <summary>Dragging a bound slot onto a different hotbar slot moves the binding rather than duplicating it.</summary>
    [TestMethod]
    public void Drag_FromABoundHotbarSlotToADifferentSlot_MovesTheBinding()
    {
        var (_, hotbarWindow, hotbar, componentManager, itemId) = BuildDragAndDropHarness();
        hotbar.BindItem(HotkeySlot.Base1, itemId);
        var controller = CreateController([], [hotbarWindow], [], [], LargeScreenSize);

        // Base is vertically centered against Expansion's current height, not flush at the top --
        // the window's own vertical center always falls inside Base1's row.
        var baseRowY = (int)hotbarWindow.ContentAbsolutePosition.Y + (int)(hotbar.Size.Y / 2f);
        Assert.IsTrue(hotbar.TryGetSlotAt(new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, baseRowY), out var sourceSlot));
        Assert.AreEqual(HotkeySlot.Base1, sourceSlot);

        var pressPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, baseRowY);
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        // Far enough along the hotbar's own width to land in a different slot -- exact slot
        // doesn't matter, only that it's not Base1. Half a slot in from the right edge (the last
        // Expansion column, row 0 -- flush at the content origin's Y) rather than the very last
        // pixel of the bar, which is fragile against float-to-int rounding across many slots/gaps.
        var dropPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + (int)(hotbar.Size.X - HotbarContent.SlotSize.X / 2f), (int)hotbarWindow.ContentAbsolutePosition.Y + 1);
        controller.Update(NoKeys, MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        Assert.IsFalse(ItemHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), 1, HotkeySlot.Base1, out _), "The origin slot must no longer be bound once the item has moved elsewhere.");
        Assert.IsTrue(hotbar.TryGetSlotAt(dropPoint, out var dropSlot));
        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), 1, dropSlot, out var boundItemId));
        Assert.AreEqual(itemId, boundItemId);
    }

    /// <summary>A plain click (press and release at the same spot, well under the tap threshold) on an already-bound slot must not unbind it -- only an actual drag should.</summary>
    [TestMethod]
    public void ClickingABoundHotbarSlot_WithoutDragging_LeavesTheBindingUnchanged()
    {
        var (_, hotbarWindow, hotbar, componentManager, itemId) = BuildDragAndDropHarness();
        hotbar.BindItem(HotkeySlot.Base1, itemId);
        var controller = CreateController([], [hotbarWindow], [], [], LargeScreenSize);

        // Base is vertically centered against Expansion's current height, not flush at the top --
        // the window's own vertical center always falls inside Base1's row.
        var point = new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, (int)hotbarWindow.ContentAbsolutePosition.Y + (int)(hotbar.Size.Y / 2f));
        controller.Update(NoKeys, MouseAt(point.X, point.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(point.X, point.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(point.X, point.Y, ButtonState.Released));

        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), 1, HotkeySlot.Base1, out var boundItemId));
        Assert.AreEqual(itemId, boundItemId);
    }

    [TestMethod]
    public void Drag_FromInventoryCell_TurnsOnHotbarDragHighlight_UntilReleased()
    {
        var (cell, hotbarWindow, hotbar, _, _) = BuildDragAndDropHarness();
        var controller = CreateController([cell], [hotbarWindow], [], [], LargeScreenSize);
        Assert.IsFalse(hotbar.IsAcceptingDrag);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.IsTrue(hotbar.IsAcceptingDrag);

        controller.Update(NoKeys, MouseAt(1000, 1000, ButtonState.Released));
        Assert.IsFalse(hotbar.IsAcceptingDrag);
    }

    /// <summary>
    /// Builds two real InventoryGridContent-hosting windows (not a hand-built standalone cell,
    /// like BuildDragAndDropHarness's hotbar harness above -- this needs InventoryItemStackCell's
    /// own EntityId, which only InventoryGridContent.RebuildCells actually sets) for two distinct
    /// entities, one item stack granted to SourceEntityId, positioned far enough apart that any
    /// press-then-release pair between them exceeds ContentDragTapThresholdPixels.
    /// </summary>
    private static (Window SourceGridWindow, Window DestinationGridWindow, InventoryItemStackCell Cell, ComponentManager ComponentManager, Guid ItemId, int SourceEntityId, int DestinationEntityId) BuildInventoryToInventoryDragHarness()
    {
        const int sourceEntityId = 1;
        const int destinationEntityId = 2;

        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);

        var itemId = Guid.NewGuid();
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(itemId, "Test Item", null, "t", Color.White, Tags: [], Effects: []));
        InventoryActions.AddItem(componentManager, sourceEntityId, itemId, quantity: 1);

        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, glyphRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, glyphRenderer));
        var hoverPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        hoverPopup.Initialize();

        Window BuildGridWindow(int entityId, Vector2 position)
        {
            var window = windowService.CreateElement<Window>(null, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
                Layout = new ElementLayoutOptions { RelativePosition = position, Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
            });
            window.SetContent(new InventoryGridContent(componentManager, itemCatalog, windowService, fontService, glyphRenderer, spriteSheetService, spriteRenderer, entityId, filterTag: null, hoverPopup));
            window.Initialize();
            return window;
        }

        var sourceGridWindow = BuildGridWindow(sourceEntityId, new Vector2(0, 0));
        var destinationGridWindow = BuildGridWindow(destinationEntityId, new Vector2(500, 0));
        var cell = sourceGridWindow.ChildElements.OfType<InventoryItemStackCell>().Single();

        return (sourceGridWindow, destinationGridWindow, cell, componentManager, itemId, sourceEntityId, destinationEntityId);
    }

    [TestMethod]
    public void Drag_FromInventoryCellToAnotherEntitysGrid_TransfersTheStack()
    {
        var (sourceGridWindow, destinationGridWindow, cell, componentManager, itemId, sourceEntityId, destinationEntityId) = BuildInventoryToInventoryDragHarness();
        var controller = CreateController([sourceGridWindow, destinationGridWindow], [], [], [], LargeScreenSize, componentManager: componentManager, playerQuery: null);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        var dropPoint = destinationGridWindow.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(0, stacks.CountForEntity(sourceEntityId));
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, destinationEntityId, itemId, out _));
    }

    [TestMethod]
    public void Drag_FromInventoryCellBackOntoItsOwnGrid_LeavesTheStackWhereItWas()
    {
        var (sourceGridWindow, _, cell, componentManager, itemId, sourceEntityId, _) = BuildInventoryToInventoryDragHarness();
        var controller = CreateController([sourceGridWindow], [], [], [], LargeScreenSize, componentManager: componentManager, playerQuery: null);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        // Still within the same source grid window, far enough from the press point to exceed
        // ContentDragTapThresholdPixels and actually resolve as a drag, not a plain click.
        var dropPoint = new Point((int)sourceGridWindow.ContentAbsolutePosition.X + 150, (int)sourceGridWindow.ContentAbsolutePosition.Y + 150);
        controller.Update(NoKeys, MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(1, stacks.CountForEntity(sourceEntityId));
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, sourceEntityId, itemId, out _));
    }

    /// <summary>
    /// Same transfer as Drag_FromInventoryCellToAnotherEntitysGrid_TransfersTheStack, but the
    /// destination is a real InventoryManagementWindow (TabbedContent -> per-tab body window ->
    /// InventoryTabContent -> its own nested grid window) instead of a bare Window hosting
    /// InventoryGridContent directly -- reproducing the actual player-inventory structure, in case
    /// FindHostingGrid's ParentElement walk behaves differently against that deeper nesting.
    /// </summary>
    [TestMethod]
    public void Drag_FromNonPlayerEntitysGridToRealInventoryManagementWindow_TransfersTheStack()
    {
        const int sourceEntityId = 2;
        const int destinationEntityId = 1;

        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);

        var itemId = Guid.NewGuid();
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(itemId, "Test Item", null, "t", Color.White, Tags: [], Effects: []));
        InventoryActions.AddItem(componentManager, sourceEntityId, itemId, quantity: 1);

        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, glyphRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<GridControl>(() => new GridControl(fontService, windowService, glyphRenderer));
        windowService.RegisterFactory<Toggle>(() => new Toggle(fontService, windowService, glyphRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, glyphRenderer));
        windowService.RegisterFactory<InventoryManagementWindow>(() => new InventoryManagementWindow(
            fontService, windowService, glyphRenderer, spriteSheetService, spriteRenderer, componentManager, itemCatalog));

        var hoverPopup = windowService.CreateElement<Tooltip>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, MaximumSize = new Vector2(220, 220), DisplayMode = ElementDisplayMode.WrapContent, IsVisible = false },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false, CanUserClose = false },
        });
        hoverPopup.Initialize();

        var sourceGridWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(200, 200), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
        });
        sourceGridWindow.SetContent(new InventoryGridContent(componentManager, itemCatalog, windowService, fontService, glyphRenderer, spriteSheetService, spriteRenderer, sourceEntityId, filterTag: null, hoverPopup));
        sourceGridWindow.Initialize();
        var cell = sourceGridWindow.ChildElements.OfType<InventoryItemStackCell>().Single();

        var destinationWindow = windowService.CreateElement<InventoryManagementWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = new Vector2(300, 300), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = true, CanUserFocus = false },
        });
        destinationWindow.Configure(destinationEntityId, hoverPopup);
        destinationWindow.Initialize();

        var controller = CreateController([], [], [sourceGridWindow, destinationWindow], [], LargeScreenSize, componentManager: componentManager, playerQuery: null);

        var pressPoint = cell.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        var dropPoint = destinationWindow.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.AreEqual(0, stacks.CountForEntity(sourceEntityId));
        Assert.IsTrue(InventoryQueries.TryGetStack(stacks, destinationEntityId, itemId, out _));
    }

    /// <summary>Actions must be draggable between hotbar slots the same way items already are -- mirrors Drag_FromABoundHotbarSlotToADifferentSlot_MovesTheBinding for the action-bound-slot source instead of an item one.</summary>
    [TestMethod]
    public void Drag_FromABoundActionHotbarSlotToADifferentSlot_MovesTheBinding()
    {
        var (hotbarWindow, hotbar, componentManager, actionId) = BuildActionDragAndDropHarness();
        hotbar.BindAction(HotkeySlot.Base1, actionId);
        var controller = CreateController([], [hotbarWindow], [], [], LargeScreenSize);

        // Base is vertically centered against Expansion's current height, not flush at the top --
        // the window's own vertical center always falls inside Base1's row.
        var baseRowY = (int)hotbarWindow.ContentAbsolutePosition.Y + (int)(hotbar.Size.Y / 2f);
        var pressPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, baseRowY);
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        // Far enough along the hotbar's own width to land in a different slot -- exact slot
        // doesn't matter, only that it's not Base1.
        var dropPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + (int)(hotbar.Size.X - HotbarContent.SlotSize.X / 2f), (int)hotbarWindow.ContentAbsolutePosition.Y + 1);
        controller.Update(NoKeys, MouseAt(dropPoint.X, dropPoint.Y, ButtonState.Released));

        Assert.IsFalse(ActionHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ActionHotkeyBindingComponent>(), 1, HotkeySlot.Base1, out _), "The origin slot must no longer be bound once the action has moved elsewhere.");
        Assert.IsTrue(hotbar.TryGetSlotAt(dropPoint, out var dropSlot));
        Assert.IsTrue(ActionHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ActionHotkeyBindingComponent>(), 1, dropSlot, out var boundActionId));
        Assert.AreEqual(actionId, boundActionId);
    }

    [TestMethod]
    public void Drag_FromABoundActionHotbarSlotToAwayFromTheHotbar_UnbindsIt()
    {
        var (hotbarWindow, hotbar, componentManager, actionId) = BuildActionDragAndDropHarness();
        hotbar.BindAction(HotkeySlot.Base1, actionId);
        var controller = CreateController([], [hotbarWindow], [], [], LargeScreenSize);

        var pressPoint = new Point((int)hotbarWindow.ContentAbsolutePosition.X + 1, (int)hotbarWindow.ContentAbsolutePosition.Y + (int)(hotbar.Size.Y / 2f));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(1000, 1000, ButtonState.Released));

        Assert.IsFalse(ActionHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ActionHotkeyBindingComponent>(), 1, HotkeySlot.Base1, out _));
    }

    /// <summary>Hotbar-only counterpart to BuildDragAndDropHarness -- no InventoryItemStackCell needed, but the hotbar's own ActionCatalog needs a real registered action to bind/drag.</summary>
    private static (Window HotbarWindow, HotbarContent Hotbar, ComponentManager ComponentManager, Guid ActionId) BuildActionDragAndDropHarness()
    {
        const int playerEntityId = 1;

        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ActionInstanceComponent>();
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<HotkeyExpansionUnlockComponent>(static (ref existing, incoming) => existing = incoming);

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = playerEntityId };
        var actionId = Guid.NewGuid();
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(actionId, "Test Action", null, "t", Color.White, [], Effects: [ActionEffect.None],
            Activator: new DirectAction(new TargetingSpec(TargetShape.SingleTarget, Range: 1), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: null))));

        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, glyphRenderer);

        var hotbar = new HotbarContent(
            world, new MapViewState(), componentManager, new EventBus(), actionCatalog, new ItemCatalog(),
            fontService, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), new Vector2(1920, 1080));
        var hotbarWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = hotbar.Size, DisplayMode = ElementDisplayMode.Fixed },
        });
        hotbarWindow.SetContent(hotbar);
        hotbarWindow.Initialize();

        return (hotbarWindow, hotbar, componentManager, actionId);
    }
}