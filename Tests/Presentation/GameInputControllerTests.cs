using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// Covers GameInputController's mouse press/release tracking (Window Chrome Phase A0) and its
/// unified hit-test/raise-to-front/interaction-state-machine (Phase A1), via its internal
/// Update(KeyboardState, MouseState) overload -- the seam that lets a test drive synthetic
/// input without a real Keyboard/Mouse device (see InternalsVisibleTo in Presentation.csproj).
/// GameInputController had zero test coverage before Phase A0.
/// </summary>
[TestClass]
public sealed class GameInputControllerTests
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

    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    /// <summary>Records HandleRightDragStart/HandleRightDrag calls, so GameInputController's right-button wiring (hit-test on press, total-delta-since-start on every held frame) can be verified end-to-end without a real MapWindow.</summary>
    private sealed class RightDragSpyWindow(FontService fontService, WindowService windowService, GlyphRenderer glyphRenderer) : Window(fontService, windowService, glyphRenderer)
    {
        public int DragStartCallCount { get; private set; }
        public int DragEndCallCount { get; private set; }
        public List<Vector2> DragDeltas { get; } = [];

        protected override void OnRightDragStartAction() => DragStartCallCount++;
        protected override void OnRightDragAction(Vector2 totalPixelDeltaSinceStart) => DragDeltas.Add(totalPixelDeltaSinceStart);
        protected override void OnRightDragEndAction() => DragEndCallCount++;
    }

    private static RightDragSpyWindow CreateRightDragSpyWindow(WindowService windowService, FontService fontService, Vector2 relativePosition)
    {
        windowService.RegisterFactory<RightDragSpyWindow>((_, _) => new RightDragSpyWindow(fontService, windowService, new GlyphRenderer()));
        var window = windowService.CreateWindow<RightDragSpyWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
        });
        window.Initialize();
        return window;
    }

    private static Window CreateRootWindowWithCloseButton(WindowService windowService, Vector2 relativePosition)
    {
        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Test", CanUserClose = true },
        });
        window.Initialize();
        return window;
    }

    /// <summary>No CanUserClose (so no title buttons to intercept the click) -- lets a press over TitleRectangle resolve to a Move interaction instead.</summary>
    private static Window CreateMovableWindow(WindowService windowService, Vector2 relativePosition)
    {
        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Movable", CanUserMove = true },
        });
        window.Initialize();
        return window;
    }

    /// <summary>ShowBorder=true (GetResizeEdgesAt requires a border to grab) and an explicit MaximumSize well past the starting size, so growing it in a test isn't silently clamped back down (see BuildWindow's MaximumSize default).</summary>
    private static Window CreateResizableWindow(WindowService windowService, Vector2 relativePosition, Vector2? maximumSize = null, Vector2? minimumSize = null)
    {
        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions
            {
                RelativePosition = relativePosition,
                Size = new Vector2(200, 100),
                MinimumSize = minimumSize ?? Vector2.Zero,
                MaximumSize = maximumSize ?? new Vector2(600, 500),
                DisplayMode = WindowDisplayMode.Fixed,
            },
            Chrome = new WindowChromeOptions { ShowBorder = true, CanUserResize = true },
        });
        window.Initialize();
        return window;
    }

    /// <summary>Fixed-size with a much taller content than the window (see TextWindowScrollingTests for the underlying scroll-bounds math) -- just enough overflow for mouse-wheel dispatch tests to have something to scroll.</summary>
    private static TextWindow CreateScrollableTextWindow(WindowService windowService, Vector2 relativePosition)
    {
        var longText = string.Join(' ', Enumerable.Repeat("word", 200));
        var window = windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(150, 30), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Text = longText },
        });
        window.Initialize();
        return window;
    }

    /// <summary>
    /// Records every key routed via IWindowContent.HandleKeyPress and counts calls to
    /// HandleHotkeys -- used to observe GameInputController's routing (RouteKeyPressesToFocusedWindow/
    /// RouteHotkeysToFocusedWindow) without needing a real MapWindow or text-input control.
    /// </summary>
    private sealed class RecordingKeyContent : IWindowContent
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

        public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
        {
        }

        public void HandleKeyPress(Keys key) => PressedKeys.Add(key);
        public void HandleHotkeys(KeyboardState keyboardState, KeyboardState previousKeyboardState) => HotkeyCallCount++;
        public void HandleTextInput(char character) => TypedCharacters.Add(character);
    }

    /// <summary>A plain titled window with a RecordingKeyContent attached, for focus/key-routing tests.</summary>
    private static (Window Window, RecordingKeyContent Content) CreateFocusableWindowWithContent(WindowService windowService, Vector2 relativePosition)
    {
        var window = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Test" },
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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = closeButton.ButtonRectangle.Center;
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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = closeButton.ButtonRectangle.Center;
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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = closeButton.ButtonRectangle.Center;
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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = closeButton.ButtonRectangle.Center;
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
        var controller = new GameInputController([window], [], LargeScreenSize);

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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = closeButton.ButtonRectangle.Center;
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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = closeButton.ButtonRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 1000, pressPoint.Y + 1000, ButtonState.Released));

        Assert.IsFalse(closed);
    }

    /// <summary>
    /// Two root windows with non-overlapping bounds -- pressing the earlier one (index 0)
    /// must move it to the end of rootWindows, exactly like Window.RaiseToFront does for a
    /// child within a parent's own list (see GameInputController.RaiseToFront).
    /// </summary>
    [TestMethod]
    public void PressingARootWindow_RaisesItToTheEndOfRootWindows()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 400));
        var rootWindows = new List<Window> { windowA, windowB };
        var controller = new GameInputController(rootWindows, [], LargeScreenSize);

        var pressPoint = windowA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        CollectionAssert.AreEqual(new[] { windowB, windowA }, rootWindows);
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
        var controller = new GameInputController([back, front], [], LargeScreenSize);

        var pressPoint = front.TitleButtons[0].ButtonRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsTrue(frontClosed);
        Assert.IsFalse(backClosed);
    }

    /// <summary>
    /// A root window and an always-on-top window sharing identical bounds -- the always-on-top
    /// tier must win the hit-test regardless of list order, mirroring notifications always
    /// floating above ordinary windows (see the plan's two-tier design).
    /// </summary>
    [TestMethod]
    public void PressingOverlappingWindows_AlwaysOnTopWins_EvenWhenCheckedSecond()
    {
        var windowService = CreateWindowService();
        var rootWindow = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var alwaysOnTopWindow = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var rootClosed = false;
        var alwaysOnTopClosed = false;
        rootWindow.Closed += _ => rootClosed = true;
        alwaysOnTopWindow.Closed += _ => alwaysOnTopClosed = true;
        var controller = new GameInputController([rootWindow], [alwaysOnTopWindow], LargeScreenSize);

        var pressPoint = alwaysOnTopWindow.TitleButtons[0].ButtonRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.IsTrue(alwaysOnTopClosed);
        Assert.IsFalse(rootClosed);
    }

    /// <summary>Pressing a movable window's title bar (away from any button) starts a Move interaction and snapshots its position/size for the drag.</summary>
    [TestMethod]
    public void PressingATitleBar_OnAMovableWindow_StartsMoveAndCapturesDragStart()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(WindowInteractionKind.Move, controller.ActiveInteraction.Kind);
        Assert.AreSame(window, controller.ActiveInteraction.Window);
        Assert.AreEqual(window.WindowRelativePosition, controller.DragStartRelativePosition);
        Assert.AreEqual(window.WindowCurrentSize, controller.DragStartSize);
    }

    /// <summary>Moving the mouse while a drag is held recomputes DragDelta -- Phase D wires Resize the same way, via SetBounds.</summary>
    [TestMethod]
    public void HoldingAMoveDrag_UpdatesDragDeltaEachFrame()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 15, pressPoint.Y + 5, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(65, 65), window.WindowRelativePosition);
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
        var parent = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Horizontal },
            Layout = new WindowLayoutOptions { Size = new Vector2(400, 100), DisplayMode = WindowDisplayMode.Fixed },
        });
        parent.Initialize();

        var child = windowService.CreateWindow<Window>(parent, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Tiled", CanUserMove = true },
        });
        parent.AddChildWindow(child);
        var controller = new GameInputController([parent], [], LargeScreenSize);

        var pressPoint = child.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreNotEqual(WindowInteractionKind.Move, controller.ActiveInteraction.Kind);
    }

    /// <summary>Releasing the mouse ends the interaction entirely, not just PressedButton -- ActiveInteraction must go back to NotHit for the next press to start clean.</summary>
    [TestMethod]
    public void ReleasingAfterAMove_ClearsActiveInteraction()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreEqual(WindowInteractionKind.Move, controller.ActiveInteraction.Kind);

        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        Assert.AreEqual(WindowInteraction.NotHit, controller.ActiveInteraction);
    }

    /// <summary>Window Chrome Phase D: pressing a resizable window's right border edge starts a Resize interaction flagged for that edge.</summary>
    [TestMethod]
    public void PressingARightBorderEdge_OnAResizableWindow_StartsResizeWithRightEdge()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(WindowInteractionKind.Resize, controller.ActiveInteraction.Kind);
        Assert.AreEqual(ResizeEdges.Right, controller.ActiveInteraction.Edges);
    }

    /// <summary>Dragging the right edge grows the window's width by the drag delta with no position change -- the left edge stays visually fixed.</summary>
    [TestMethod]
    public void HoldingARightEdgeResize_GrowsWidthWithNoPositionChange()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 40, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(240, 100), window.WindowCurrentSize);
        Assert.AreEqual(new Vector2(50, 60), window.WindowRelativePosition);
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
        var controller = new GameInputController([window], [], LargeScreenSize);
        var rightEdgeBeforeDrag = window.WindowRelativePosition.X + window.WindowCurrentSize.X;

        var pressPoint = window.BorderLeftRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 40, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(160, 100), window.WindowCurrentSize);
        Assert.AreEqual(new Vector2(90, 60), window.WindowRelativePosition);
        Assert.AreEqual(rightEdgeBeforeDrag, window.WindowRelativePosition.X + window.WindowCurrentSize.X);
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
        var controller = new GameInputController([window], [], LargeScreenSize);
        var rightEdgeBeforeDrag = window.WindowRelativePosition.X + window.WindowCurrentSize.X;

        var pressPoint = window.BorderLeftRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        // Dragging left by 100 would grow the width to 300 (200 + 100), past the 250 maximum.
        controller.Update(NoKeys, MouseAt(pressPoint.X - 100, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(250, window.WindowCurrentSize.X);
        Assert.AreEqual(rightEdgeBeforeDrag, window.WindowRelativePosition.X + window.WindowCurrentSize.X);
    }

    /// <summary>A corner combines two edges in one drag -- bottom-right grows both dimensions independently.</summary>
    [TestMethod]
    public void HoldingABottomRightCornerResize_GrowsBothDimensions()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = new Point(window.WindowRectangle.Right - 2, window.WindowRectangle.Bottom - 2);
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreEqual(ResizeEdges.Bottom | ResizeEdges.Right, controller.ActiveInteraction.Edges);

        controller.Update(NoKeys, MouseAt(pressPoint.X + 30, pressPoint.Y + 20, ButtonState.Pressed));

        Assert.AreEqual(new Vector2(230, 120), window.WindowCurrentSize);
    }

    /// <summary>WrapContent/Fill windows compute size from content/parent, not SetSize/SetBounds -- offering a Resize interaction there would start a drag that visibly does nothing.</summary>
    [TestMethod]
    public void PressingABorderEdge_OnANonFixedResizableWindow_DoesNotStartResize()
    {
        var windowService = CreateWindowService();
        var window = windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(50, 60), MaximumSize = new Vector2(400, 300), DisplayMode = WindowDisplayMode.WrapContent },
            Chrome = new WindowChromeOptions { ShowBorder = true, CanUserResize = true },
            Text = new TextOptions { Text = "Hello" },
        });
        window.Initialize();
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreNotEqual(WindowInteractionKind.Resize, controller.ActiveInteraction.Kind);
    }

    /// <summary>A tiled child's size is recomputed on the next AddChildWindow/RemoveChildWindow -- same HasFreePosition gate as Move, applied to Resize too.</summary>
    [TestMethod]
    public void PressingABorderEdge_OnATiledChildWindow_DoesNotStartResize()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Horizontal },
            Layout = new WindowLayoutOptions { Size = new Vector2(400, 100), DisplayMode = WindowDisplayMode.Fixed },
        });
        parent.Initialize();

        var child = windowService.CreateWindow<Window>(parent, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(200, 100), MaximumSize = new Vector2(600, 500), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowBorder = true, CanUserResize = true },
        });
        parent.AddChildWindow(child);
        var controller = new GameInputController([parent], [], LargeScreenSize);

        var pressPoint = child.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreNotEqual(WindowInteractionKind.Resize, controller.ActiveInteraction.Kind);
    }

    /// <summary>New requirement: dragging must not move a root window off-screen -- it should stop at the screen's right/bottom edge instead of continuing to follow the mouse.</summary>
    [TestMethod]
    public void HoldingAMoveDrag_StopsAtTheScreensRightAndBottomEdges()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var smallScreen = new Vector2(300, 200);
        var controller = new GameInputController([window], [], smallScreen);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 1000, pressPoint.Y + 1000, ButtonState.Pressed));

        // 200x100 window on a 300x200 screen: furthest it can go is (100, 100).
        Assert.AreEqual(new Vector2(100, 100), window.WindowRelativePosition);
    }

    /// <summary>The other side of the same requirement -- dragging toward/past the top-left must stop at (0, 0), not go negative.</summary>
    [TestMethod]
    public void HoldingAMoveDrag_StopsAtTheScreensTopAndLeftEdges()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X - 1000, pressPoint.Y - 1000, ButtonState.Pressed));

        Assert.AreEqual(Vector2.Zero, window.WindowRelativePosition);
    }

    /// <summary>
    /// A child window must stay within its parent's content rectangle, not just the screen --
    /// the same clamp, just against a different bound (GetPositionBounds).
    /// </summary>
    [TestMethod]
    public void HoldingAMoveDrag_OnAChildWindow_StopsAtTheParentsContentEdges()
    {
        var windowService = CreateWindowService();
        var parent = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Floating },
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 200), DisplayMode = WindowDisplayMode.Fixed },
        });
        parent.Initialize();
        var child = windowService.CreateWindow<Window>(parent, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(10, 10), Size = new Vector2(50, 50), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Child", CanUserMove = true },
        });
        parent.AddChildWindow(child);
        var controller = new GameInputController([parent], [], LargeScreenSize);

        var pressPoint = child.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 1000, pressPoint.Y + 1000, ButtonState.Pressed));

        Assert.AreEqual(parent.ContentSize - child.WindowCurrentSize, child.WindowRelativePosition);
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
        var controller = new GameInputController([window], [], smallScreen);
        var rightEdgeBeforeDrag = window.WindowRelativePosition.X + window.WindowCurrentSize.X;

        var pressPoint = window.BorderLeftRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X - 1000, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(0, window.WindowRelativePosition.X);
        Assert.AreEqual(rightEdgeBeforeDrag, window.WindowRelativePosition.X + window.WindowCurrentSize.X);
    }

    /// <summary>Growing the right edge past the screen's right boundary must shrink to fit rather than pushing the window (or its right edge) off-screen.</summary>
    [TestMethod]
    public void HoldingARightEdgeResize_StopsAtTheScreensRightEdge()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60), maximumSize: new Vector2(1000, 1000));
        var smallScreen = new Vector2(300, 200);
        var controller = new GameInputController([window], [], smallScreen);

        var pressPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        controller.Update(NoKeys, MouseAt(pressPoint.X + 1000, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(300, window.WindowRelativePosition.X + window.WindowCurrentSize.X);
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
        var controller = new GameInputController([window], [], LargeScreenSize);

        // 5px in from the right edge, vertically centered (well clear of the corner zones).
        var pressPoint = new Point(window.WindowRectangle.Right - 5, window.WindowRectangle.Y + (int)(window.WindowCurrentSize.Y / 2));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));

        Assert.AreEqual(WindowInteractionKind.Resize, controller.ActiveInteraction.Kind);
        Assert.AreEqual(ResizeEdges.Right, controller.ActiveInteraction.Edges);
    }

    /// <summary>New requirement: hovering (no press) over a resize handle sets the matching directional OS cursor.</summary>
    [TestMethod]
    public void HoveringOverARightBorderEdge_SetsTheSizeWECursor()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var hoverPoint = window.BorderRightRectangle.Center;
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeWE, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverATopBorderEdge_SetsTheSizeNSCursor()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var hoverPoint = window.BorderTopRectangle.Center;
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeNS, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverATopLeftCorner_SetsTheSizeNWSECursor()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var hoverPoint = new Point(window.WindowRectangle.X + 2, window.WindowRectangle.Y + 2);
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeNWSE, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverATopRightCorner_SetsTheSizeNESWCursor()
    {
        var windowService = CreateWindowService();
        var window = CreateResizableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var hoverPoint = new Point(window.WindowRectangle.Right - 2, window.WindowRectangle.Y + 2);
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeNESW, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverAMovableWindowsTitleBar_SetsTheSizeAllCursor()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var hoverPoint = window.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(hoverPoint.X, hoverPoint.Y, ButtonState.Released));

        Assert.AreEqual(MouseCursor.SizeAll, controller.CurrentCursor);
    }

    [TestMethod]
    public void HoveringOverPlainContent_SetsTheArrowCursor()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

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
        var controller = new GameInputController([window], [], LargeScreenSize);

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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var hoverPoint = window.ContentRectangle.Center;
        // -120 = one notch "down" (the FNA/XNA convention -- see GameInputController.UpdateMouseWheelScroll), which should scroll forward into the content (ScrollOffset increases), matching every other app's convention.
        controller.Update(NoKeys, MouseAtWithScroll(hoverPoint.X, hoverPoint.Y, -120));

        Assert.IsGreaterThan(0, window.ScrollOffset.Y);
    }

    /// <summary>Regression guard: hovering a window that never opted into scrolling (CanUserScrollVertical/Horizontal both false) must not scroll it, even with an active wheel delta.</summary>
    [TestMethod]
    public void MouseWheel_OverANonScrollableWindow_DoesNothing()
    {
        var windowService = CreateWindowService();
        var window = CreateMovableWindow(windowService, new Vector2(50, 60));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var hoverPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithScroll(hoverPoint.X, hoverPoint.Y, -120));

        Assert.AreEqual(Vector2.Zero, window.ScrollOffset);
    }

    /// <summary>
    /// Right-press hit-tests (like a left-click) to find the drag's target and fires
    /// HandleRightDragStart on it exactly once; every held frame afterward reports the total
    /// pixel delta since the drag started (not a per-frame increment) via HandleRightDrag.
    /// This is the actual GameInputController-to-Window wiring a real MapWindow depends on for
    /// camera panning -- MapWindowTests only covers OnRightDragAction's own math in isolation.
    /// </summary>
    [TestMethod]
    public void RightMouseDrag_ReportsTotalDeltaSinceStart_ToTheWindowUnderTheCursor()
    {
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = new GameInputController([window], [], LargeScreenSize);

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
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = new GameInputController([window], [], LargeScreenSize);

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
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var window = CreateRightDragSpyWindow(windowService, fontService, new Vector2(0, 0));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 10, pressPoint.Y, ButtonState.Pressed));
        Assert.AreEqual(0, window.DragEndCallCount, "Must not fire while the drag is still held.");

        controller.Update(NoKeys, MouseAtWithRightButton(pressPoint.X - 10, pressPoint.Y, ButtonState.Released));

        Assert.AreEqual(1, window.DragEndCallCount);
    }

    /// <summary>Right-dragging over empty space (nothing hit) must not throw -- it simply has nowhere to forward to until released.</summary>
    [TestMethod]
    public void RightMouseDrag_OverEmptySpace_DoesNotThrow()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = new GameInputController([window], [], LargeScreenSize);

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
        var controller = new GameInputController([windowA, windowB], [], LargeScreenSize);

        var pressPointA = windowA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointA.X, pressPointA.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointA.X, pressPointA.Y, ButtonState.Pressed));

        Assert.AreSame(windowA, controller.FocusedWindow);
        Assert.IsTrue(windowA.IsFocused);
        Assert.IsFalse(windowB.IsFocused);

        var pressPointB = windowB.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointB.X, pressPointB.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointB.X, pressPointB.Y, ButtonState.Pressed));

        Assert.AreSame(windowB, controller.FocusedWindow);
        Assert.IsTrue(windowB.IsFocused);
        Assert.IsFalse(windowA.IsFocused);
    }

    /// <summary>Pressing where nothing is hit (see the mouse-press branch's own Window-is-null guard) must leave whatever was already focused alone, not clear it.</summary>
    [TestMethod]
    public void ClickingEmptySpace_LeavesFocusUnchanged()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(window, controller.FocusedWindow);

        controller.Update(NoKeys, MouseAt(1900, 1900, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(1900, 1900, ButtonState.Pressed));

        Assert.AreSame(window, controller.FocusedWindow);
        Assert.IsTrue(window.IsFocused);
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
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(window, controller.FocusedWindow);

        window.Close();

        Assert.IsNull(controller.FocusedWindow);
    }

    /// <summary>A key newly pressed with a window focused reaches that window's content, not an unfocused sibling's -- the generic routing pipeline Text Input will eventually build on.</summary>
    [TestMethod]
    public void PressingAKey_RoutesOnlyToTheFocusedWindowsContent()
    {
        var windowService = CreateWindowService();
        var (focused, focusedContent) = CreateFocusableWindowWithContent(windowService, new Vector2(0, 0));
        var (other, otherContent) = CreateFocusableWindowWithContent(windowService, new Vector2(400, 400));
        var controller = new GameInputController([focused, other], [], LargeScreenSize);

        var pressPoint = focused.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        controller.Update(new KeyboardState(Keys.A), MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));

        CollectionAssert.AreEqual(new[] { Keys.A }, focusedContent.PressedKeys);
        Assert.HasCount(0, otherContent.PressedKeys);
    }

    /// <summary>
    /// GameInputController knows nothing about what any window's hotkeys actually are -- it
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
        var controller = new GameInputController([focused], [], LargeScreenSize);

        var pressPoint = focused.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(focused, controller.FocusedWindow);

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
        var controller = new GameInputController([focused, other], [], LargeScreenSize);

        var pressPoint = focused.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(focused, controller.FocusedWindow);

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
        var controller = new GameInputController([window], [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));

        Assert.AreEqual(0, content.HotkeyCallCount);
    }

    /// <summary>
    /// A typed character (simulated via OnTextInput -- the internal seam a real
    /// TextInputEXT.TextInput subscription feeds in production, see the GameInputController
    /// constructor) reaches only the focused window's content, mirroring HandleKeyPress/
    /// HandleHotkeys routing.
    /// </summary>
    [TestMethod]
    public void TypedCharacters_RouteOnlyToTheFocusedWindowsContent()
    {
        var windowService = CreateWindowService();
        var (focused, focusedContent) = CreateFocusableWindowWithContent(windowService, new Vector2(0, 0));
        var (other, otherContent) = CreateFocusableWindowWithContent(windowService, new Vector2(400, 400));
        var controller = new GameInputController([focused, other], [], LargeScreenSize);

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
        var controller = new GameInputController([focused], [], LargeScreenSize);
        controller.FocusWindow(focused);

        controller.OnTextInput('h');
        controller.OnTextInput('i');
        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));

        CollectionAssert.AreEqual(new[] { 'h', 'i' }, focusedContent.TypedCharacters);
    }

    /// <summary>
    /// A window with a focusable TextBox child is never itself the terminal focus target --
    /// focusing it (a click, here) redirects into its first TextBox instead, per
    /// GameInputController.SetFocus's NextTextBoxAfter redirect.
    /// </summary>
    [TestMethod]
    public void ClickingAWindowWithATextBoxChild_FocusesTheTextBoxInstead()
    {
        var windowService = CreateWindowService();
        var container = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true },
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Form", CanUserMove = true },
        });
        container.Initialize();
        var textBox = windowService.CreateWindow<TextBox>(container, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(180, 50), DisplayMode = WindowDisplayMode.Fixed },
        });
        container.AddChildWindow(textBox);
        var controller = new GameInputController([container], [], LargeScreenSize);

        // Click the container's title bar -- content-agnostic, so it can't be mistaken for
        // directly clicking the TextBox child itself.
        var titlePoint = container.TitleRectangle.Center;
        controller.Update(NoKeys, MouseAt(titlePoint.X, titlePoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(titlePoint.X, titlePoint.Y, ButtonState.Pressed));

        Assert.AreSame(textBox, controller.FocusedWindow);
        Assert.IsTrue(textBox.IsFocused);
        Assert.IsFalse(container.IsFocused);
    }

    /// <summary>Enter on a TextBox with a sibling TextBox asks GameInputController (via FocusRequested) to move focus to it -- the same mechanism a click or Tab would use.</summary>
    [TestMethod]
    public void SubmittingATextBox_MovesFocusToTheNextTextBoxSibling()
    {
        var windowService = CreateWindowService();
        var container = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true },
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(200, 200), DisplayMode = WindowDisplayMode.Fixed },
        });
        container.Initialize();
        var firstTextBox = windowService.CreateWindow<TextBox>(container, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(0, 0), Size = new Vector2(180, 50), DisplayMode = WindowDisplayMode.Fixed },
        });
        container.AddChildWindow(firstTextBox);
        var secondTextBox = windowService.CreateWindow<TextBox>(container, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(0, 60), Size = new Vector2(180, 50), DisplayMode = WindowDisplayMode.Fixed },
        });
        container.AddChildWindow(secondTextBox);
        var controller = new GameInputController([container], [], LargeScreenSize);
        controller.FocusWindow(firstTextBox);

        controller.Update(new KeyboardState(Keys.Enter), MouseAt(0, 0, ButtonState.Released));

        Assert.AreSame(secondTextBox, controller.FocusedWindow);
    }

    /// <summary>Tab advances focus to the next root window, wrapping past the last one back to the first -- rootWindows only, in list order.</summary>
    [TestMethod]
    public void PressingTab_CyclesFocusThroughRootWindows_Wrapping()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 400));
        var rootWindows = new List<Window> { windowA, windowB };
        var controller = new GameInputController(rootWindows, [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));
        Assert.AreSame(windowA, controller.FocusedWindow);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));
        Assert.AreSame(windowB, controller.FocusedWindow);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));
        Assert.AreSame(windowA, controller.FocusedWindow);
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
        var rootWindows = new List<Window> { windowA, windowB };
        var controller = new GameInputController(rootWindows, [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));

        CollectionAssert.AreEqual(new[] { windowA, windowB }, rootWindows);
    }

    /// <summary>Shift+Tab cycles the other direction.</summary>
    [TestMethod]
    public void PressingShiftTab_CyclesFocusBackward()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 400));
        var rootWindows = new List<Window> { windowA, windowB };
        var controller = new GameInputController(rootWindows, [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab, Keys.LeftShift), MouseAt(0, 0, ButtonState.Released));

        Assert.AreSame(windowB, controller.FocusedWindow);
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
        var rootWindows = new List<Window> { windowA, windowB, windowC };
        var controller = new GameInputController(rootWindows, [], LargeScreenSize);

        var visited = new List<Window>();
        for (var i = 0; i < 6; i++)
        {
            controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
            controller.Update(new KeyboardState(Keys.Tab, Keys.LeftShift), MouseAt(0, 0, ButtonState.Released));
            visited.Add(controller.FocusedWindow!);
        }

        // Backward from unfocused starts at the last window (C), then keeps stepping backward,
        // wrapping: C, B, A, C, B, A.
        CollectionAssert.AreEqual(new[] { windowC, windowB, windowA, windowC, windowB, windowA }, visited);
    }

    /// <summary>A window with CanUserFocus = false (e.g. the debug stats window) is a concrete opt-out: clicking it must not change focus at all.</summary>
    [TestMethod]
    public void ClickingANonFocusableWindow_DoesNotChangeFocus()
    {
        var windowService = CreateWindowService();
        var focusable = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var nonFocusable = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(400, 400), Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserFocus = false },
        });
        nonFocusable.Initialize();
        var controller = new GameInputController([focusable, nonFocusable], [], LargeScreenSize);

        var pressPointFocusable = focusable.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointFocusable.X, pressPointFocusable.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointFocusable.X, pressPointFocusable.Y, ButtonState.Pressed));
        Assert.AreSame(focusable, controller.FocusedWindow);

        var pressPointNonFocusable = nonFocusable.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPointNonFocusable.X, pressPointNonFocusable.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPointNonFocusable.X, pressPointNonFocusable.Y, ButtonState.Pressed));

        Assert.AreSame(focusable, controller.FocusedWindow);
        Assert.IsFalse(nonFocusable.IsFocused);
    }

    /// <summary>A CanUserFocus = false window (e.g. the debug stats window) is skipped entirely by Tab -- it's never a stop in the cycle.</summary>
    [TestMethod]
    public void PressingTab_SkipsNonFocusableRootWindows()
    {
        var windowService = CreateWindowService();
        var windowA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var nonFocusable = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(400, 0), Size = new Vector2(200, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserFocus = false },
        });
        nonFocusable.Initialize();
        var windowB = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 400));
        var rootWindows = new List<Window> { windowA, nonFocusable, windowB };
        var controller = new GameInputController(rootWindows, [], LargeScreenSize);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));
        Assert.AreSame(windowA, controller.FocusedWindow);

        controller.Update(NoKeys, MouseAt(0, 0, ButtonState.Released));
        controller.Update(new KeyboardState(Keys.Tab), MouseAt(0, 0, ButtonState.Released));

        Assert.AreSame(windowB, controller.FocusedWindow);
        Assert.IsFalse(nonFocusable.IsFocused);
    }

    private static (Window Parent, Window ChildA, Window ChildB) CreateParentWithTwoCloseableChildren(WindowService windowService)
    {
        var parent = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Vertical },
            Layout = new WindowLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(300, 200), DisplayMode = WindowDisplayMode.Fixed },
        });
        parent.Initialize();

        var childA = windowService.CreateWindow<Window>(parent, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 50), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "A", CanUserClose = true },
        });
        parent.AddChildWindow(childA);

        var childB = windowService.CreateWindow<Window>(parent, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 50), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "B", CanUserClose = true },
        });
        parent.AddChildWindow(childB);

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
        var controller = new GameInputController([], [notificationA, notificationB], LargeScreenSize);

        var pressPoint = notificationA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(notificationA, controller.FocusedWindow);

        notificationA.Close();

        Assert.AreSame(notificationB, controller.FocusedWindow);
    }

    private static (Window Popup, Window Child) CreatePopupWithFocusableChild(WindowService windowService, Vector2 relativePosition)
    {
        var popup = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true },
            Layout = new WindowLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(300, 200), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { ShowTitle = true, TitleText = "Popup", CanUserClose = true },
        });
        popup.Initialize();

        var child = windowService.CreateWindow<Window>(popup, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(280, 150), DisplayMode = WindowDisplayMode.Fixed },
        });
        popup.AddChildWindow(child);

        return (popup, child);
    }

    /// <summary>
    /// Regression test for the reported bug: closing the quest-composer popup while its
    /// TextBox child holds focus left focus stranded on the (now closed/hidden) TextBox
    /// instead of falling back to the map window -- popup.Close() only ever fires Closed on
    /// popup itself, never on the still-focused child, so a redirect wired to just the exact
    /// focused window's own Closed event never saw it happen at all. GameInputController now
    /// subscribes Closed across the focused window's whole ancestor chain (see
    /// _focusedWindowAncestorChain), not just the focused window itself.
    /// </summary>
    [TestMethod]
    public void ClosingAWindowWhoseFocusedChildIsNotItself_StillRedirectsFocusAwayFromTheChild()
    {
        var windowService = CreateWindowService();
        var (popup, child) = CreatePopupWithFocusableChild(windowService, new Vector2(0, 0));
        var mapWindowStandIn = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 0));
        var controller = new GameInputController([popup, mapWindowStandIn], [], LargeScreenSize);
        controller.SetDefaultFocusWindow(mapWindowStandIn);

        var pressPoint = child.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(child, controller.FocusedWindow);

        popup.Close();

        Assert.AreSame(mapWindowStandIn, controller.FocusedWindow);
    }

    /// <summary>Same redirect, generalized to sibling child windows under a shared parent (e.g. a future multi-pane form), not just the always-on-top notification stack.</summary>
    [TestMethod]
    public void ClosingTheFocusedChildWindow_RedirectsFocusToItsSiblingChild()
    {
        var windowService = CreateWindowService();
        var (parent, childA, childB) = CreateParentWithTwoCloseableChildren(windowService);
        var controller = new GameInputController([parent], [], LargeScreenSize);

        var pressPoint = childA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(childA, controller.FocusedWindow);

        childA.Close();

        Assert.AreSame(childB, controller.FocusedWindow);
    }

    /// <summary>Same trigger, but via minimizing (WindowMinimizeRestoreBehavior's real WindowDisplayMode.Minimized toggle) instead of closing -- a minimized window reads as "no longer active" the same way a closed one does.</summary>
    [TestMethod]
    public void MinimizingTheFocusedWindow_RedirectsFocusToTheRemainingSibling()
    {
        var windowService = CreateWindowService();
        var notificationA = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var notificationB = CreateRootWindowWithCloseButton(windowService, new Vector2(300, 0));
        var controller = new GameInputController([], [notificationA, notificationB], LargeScreenSize);

        var pressPoint = notificationA.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(notificationA, controller.FocusedWindow);

        notificationA.SetWindowDisplayMode(WindowDisplayMode.Minimized);

        Assert.AreSame(notificationB, controller.FocusedWindow);
    }

    /// <summary>Regression guard: DisplayModeChanged fires on every mode change, not just transitions into Minimized -- an unrelated mode change (e.g. Fixed to Fill) must not spuriously redirect focus away.</summary>
    [TestMethod]
    public void ChangingTheFocusedWindowsDisplayModeToSomethingOtherThanMinimized_DoesNotRedirectFocus()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(window, controller.FocusedWindow);

        window.SetWindowDisplayMode(WindowDisplayMode.Fill);

        Assert.AreSame(window, controller.FocusedWindow);
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
        var controller = new GameInputController([popup, unrelatedRootWindow, mapWindowStandIn], [], LargeScreenSize);
        controller.SetDefaultFocusWindow(mapWindowStandIn);

        var pressPoint = popup.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(popup, controller.FocusedWindow);

        popup.Close();

        Assert.AreSame(mapWindowStandIn, controller.FocusedWindow);
    }

    /// <summary>Regression test for task 2's other half: with no default focus window configured (e.g. a test that never calls SetDefaultFocusWindow) and no eligible sibling, closing the focused window still just clears focus rather than throwing.</summary>
    [TestMethod]
    public void ClosingTheOnlyFocusedWindow_WithNoDefaultFocusWindowConfigured_LeavesFocusNull()
    {
        var windowService = CreateWindowService();
        var window = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var controller = new GameInputController([window], [], LargeScreenSize);

        var pressPoint = window.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(window, controller.FocusedWindow);

        window.Close();

        Assert.IsNull(controller.FocusedWindow);
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
        var summaryBarStandIn = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = new Vector2(0, 300), Size = new Vector2(200, 30), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserFocus = false },
        });
        summaryBarStandIn.Initialize();
        var notification = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 0));
        var mapWindowStandIn = CreateRootWindowWithCloseButton(windowService, new Vector2(0, 400));
        var controller = new GameInputController([], [summaryBarStandIn, notification], LargeScreenSize);
        controller.SetDefaultFocusWindow(mapWindowStandIn);

        var pressPoint = notification.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(pressPoint.X, pressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(notification, controller.FocusedWindow);

        notification.Close();

        Assert.AreSame(mapWindowStandIn, controller.FocusedWindow);
    }

    private static TextBox CreateFocusableTextBox(WindowService windowService, Vector2 relativePosition)
    {
        var textBox = windowService.CreateWindow<TextBox>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(200, 60), DisplayMode = WindowDisplayMode.Fixed },
        });
        textBox.Initialize();
        return textBox;
    }

    /// <summary>
    /// Regression test: SDL text-input mode (which gates OS IME composition popups, and on
    /// touch/mobile SDL backends the on-screen keyboard) must track TextBox focus rather than
    /// run for the whole app session -- started only once an actual TextBox gains focus, and
    /// stopped again once focus moves to anything that isn't one. Substitutes call-recording
    /// fakes for StartTextInput/StopTextInput (see GameInputController) rather than asserting
    /// on TextInputEXT.IsTextInputActive() directly -- that reads real SDL state, which isn't
    /// reliably observable with no actual SDL window backing this headless test environment.
    /// </summary>
    [TestMethod]
    public void FocusMovingToAndAwayFromATextBox_TogglesSdlTextInputMode()
    {
        var windowService = CreateWindowService();
        var textBox = CreateFocusableTextBox(windowService, new Vector2(0, 0));
        var plainWindow = CreateRootWindowWithCloseButton(windowService, new Vector2(400, 0));
        var controller = new GameInputController([textBox, plainWindow], [], LargeScreenSize);
        var startCount = 0;
        var stopCount = 0;
        controller.StartTextInput = () => startCount++;
        controller.StopTextInput = () => stopCount++;

        var textBoxPressPoint = textBox.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(textBoxPressPoint.X, textBoxPressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(textBoxPressPoint.X, textBoxPressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(textBox, controller.FocusedWindow);
        Assert.AreEqual(1, startCount);
        Assert.AreEqual(0, stopCount);

        var plainWindowPressPoint = plainWindow.ContentRectangle.Center;
        controller.Update(NoKeys, MouseAt(plainWindowPressPoint.X, plainWindowPressPoint.Y, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(plainWindowPressPoint.X, plainWindowPressPoint.Y, ButtonState.Pressed));
        Assert.AreSame(plainWindow, controller.FocusedWindow);
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
        var controller = new GameInputController([windowA, windowB], [], LargeScreenSize);
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
}