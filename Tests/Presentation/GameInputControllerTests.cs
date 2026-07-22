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

    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

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
}
