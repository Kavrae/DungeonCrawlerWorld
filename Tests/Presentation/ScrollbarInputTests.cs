using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class ScrollbarInputTests
{
    private static readonly KeyboardState NoKeys = new();
    private static readonly Vector2 LargeScreenSize = new(2000, 2000);
    private const float RowHeight = 20f;

    private static ElementPoolService CreateWindowService() => TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());

    private static MouseState MouseAt(Point position, ButtonState leftButton) =>
        new(position.X, position.Y, 0, leftButton, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static UiInputController CreateController(params Element[] staticHudElements)
    {
        var layers = new UiLayerStack();
        foreach (var element in staticHudElements)
        {
            layers.Add(UiLayer.StaticHud, element);
        }
        return new UiInputController(layers, LargeScreenSize);
    }

    private static Window CreateColumn(ElementPoolService windowService, Element? parent, Vector2 relativePosition, Vector2 size)
    {
        var column = windowService.CreateElement<Window>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = parent is null, ShowTitle = false, CanUserScrollVertical = true, CanUserFocus = false },
        });

        if (parent is null)
        {
            column.Initialize();
        }
        else
        {
            parent.AddChild(column);
        }

        for (var index = 0; index < 20; index++)
        {
            var row = windowService.CreateElement<TextWindow>(column, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(column.ContentSize.X, RowHeight), MaximumSize = new Vector2(column.ContentSize.X, 10_000), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Text = new TextOptions { Text = $"Row {index}" },
            });
            column.AddChild(row);
        }

        return column;
    }

    private static Window CreateRootColumn(ElementPoolService windowService) => CreateColumn(windowService, null, new Vector2(100, 100), new Vector2(200, 100));

    private static void Move(UiInputController controller, Point position) => controller.Update(NoKeys, MouseAt(position, ButtonState.Released));

    private static void Press(UiInputController controller, Point position)
    {
        controller.Update(NoKeys, MouseAt(position, ButtonState.Released));
        controller.Update(NoKeys, MouseAt(position, ButtonState.Pressed));
    }

    private static void Hold(UiInputController controller, Point position, int frames = 1)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            controller.Update(NoKeys, MouseAt(position, ButtonState.Pressed));
        }
    }

    private static void Release(UiInputController controller, Point position) => controller.Update(NoKeys, MouseAt(position, ButtonState.Released));

    private static Point Offset(Point point, int x, int y) => new(point.X + x, point.Y + y);

    [TestMethod]
    public void PressOnThumb_StartsAThumbDrag()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);

        Press(controller, column.VerticalScrollbarThumbRectangle.Center);

        Assert.AreEqual(ElementDragInteractionKind.ScrollThumb, controller.ActiveInteraction.Kind);
        Assert.AreSame(column, controller.ActiveInteraction.Element);
        Assert.AreEqual(ScrollbarPart.VerticalThumb, column.ScrollbarPressedPart);
    }

    [TestMethod]
    public void DraggingTheThumb_ScrollsProportionallyToItsTravel()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        var thumbTravel = column.VerticalScrollbarTrackRectangle.Height - column.VerticalScrollbarThumbRectangle.Height;
        var start = column.VerticalScrollbarThumbRectangle.Center;

        Press(controller, start);
        Hold(controller, Offset(start, 0, 10));

        Assert.AreEqual(10f * column.MaxScrollOffset.Y / thumbTravel, column.ScrollOffset.Y, 0.01f);
    }

    [TestMethod]
    public void DraggingTheThumbPastEitherEnd_ClampsTheScroll()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        var start = column.VerticalScrollbarThumbRectangle.Center;

        Press(controller, start);
        Hold(controller, Offset(start, 0, 1000));
        Assert.AreEqual(column.MaxScrollOffset.Y, column.ScrollOffset.Y);

        Hold(controller, Offset(start, 0, -1000));
        Assert.AreEqual(0f, column.ScrollOffset.Y);
    }

    [TestMethod]
    public void DraggingTheThumbSideways_DoesNotScroll()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        var start = column.VerticalScrollbarThumbRectangle.Center;

        Press(controller, start);
        Hold(controller, Offset(start, 200, 0));

        Assert.AreEqual(Vector2.Zero, column.ScrollOffset);
    }

    [TestMethod]
    public void ReleasingAThumbDrag_NeverClicksTheScrolledElement_AndClearsThePressedPart()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        var clickCount = 0;
        column.Clicked += _ => clickCount++;
        var start = column.VerticalScrollbarThumbRectangle.Center;

        Press(controller, start);
        Release(controller, start);

        Assert.AreEqual(0, clickCount);
        Assert.AreEqual(ScrollbarPart.None, column.ScrollbarPressedPart);
        Assert.AreEqual(ElementDragInteractionKind.None, controller.ActiveInteraction.Kind);
    }

    [TestMethod]
    public void PressOnTrackBelowTheThumb_PagesDownOneVisibleHeight()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        var track = column.VerticalScrollbarTrackRectangle;
        var belowThumb = new Point(track.Center.X, track.Bottom - 2);

        Press(controller, belowThumb);

        Assert.AreEqual(ElementDragInteractionKind.ScrollTrack, controller.ActiveInteraction.Kind);
        Assert.AreEqual(System.Math.Min(column.ContentSize.Y, column.MaxScrollOffset.Y), column.ScrollOffset.Y);
    }

    [TestMethod]
    public void PressOnTrackAboveTheThumb_PagesUp()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        column.ScrollBy(new Vector2(0, column.MaxScrollOffset.Y));
        var track = column.VerticalScrollbarTrackRectangle;

        Press(controller, new Point(track.Center.X, track.Top + 1));

        Assert.AreEqual(System.Math.Max(0f, column.MaxScrollOffset.Y - column.ContentSize.Y), column.ScrollOffset.Y);
    }

    [TestMethod]
    public void HoldingOnTheTrackBrieflyPagesOnlyOnce()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        var track = column.VerticalScrollbarTrackRectangle;
        var belowThumb = new Point(track.Center.X, track.Bottom - 2);

        Press(controller, belowThumb);
        var offsetAfterPress = column.ScrollOffset.Y;
        Hold(controller, belowThumb, frames: 10);

        Assert.AreEqual(offsetAfterPress, column.ScrollOffset.Y);
    }

    [TestMethod]
    public void HoldingOnTheTrack_RepeatsUntilTheThumbReachesTheCursor()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        var track = column.VerticalScrollbarTrackRectangle;
        var nearBottom = new Point(track.Center.X, track.Bottom - 2);

        Press(controller, nearBottom);
        Hold(controller, nearBottom, frames: 600);

        Assert.AreEqual(column.MaxScrollOffset.Y, column.ScrollOffset.Y);
        Assert.IsTrue(column.VerticalScrollbarThumbRectangle.Contains(nearBottom));
    }

    [TestMethod]
    public void HoldingOnTheTrackMidway_StopsOnceTheThumbIsUnderTheCursor()
    {
        var windowService = CreateWindowService();
        var column = CreateColumn(windowService, null, new Vector2(100, 100), new Vector2(200, 300));
        for (var index = 0; index < 200; index++)
        {
            column.AddChild(windowService.CreateElement<TextWindow>(column, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = new Vector2(column.ContentSize.X, RowHeight), MaximumSize = new Vector2(column.ContentSize.X, 10_000), DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false },
                Text = new TextOptions { Text = "Extra" },
            }));
        }
        var controller = CreateController(column);
        var track = column.VerticalScrollbarTrackRectangle;
        var midway = new Point(track.Center.X, track.Center.Y);

        Press(controller, midway);
        Hold(controller, midway, frames: 600);

        Assert.IsLessThan(column.MaxScrollOffset.Y, column.ScrollOffset.Y);
        var thumb = column.VerticalScrollbarThumbRectangle;
        Assert.IsLessThanOrEqualTo(midway.Y + 1, thumb.Top);
        Assert.IsGreaterThan(midway.Y, thumb.Bottom);
    }

    [TestMethod]
    public void HoldingOnTheTrack_AfterTheThumbPassesTheCursor_NeverPagesBack()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);
        var track = column.VerticalScrollbarTrackRectangle;
        var belowThumb = new Point(track.Center.X, track.Bottom - 2);
        Press(controller, belowThumb);
        var offsetAfterPress = column.ScrollOffset.Y;
        var aboveTheMovedThumb = new Point(track.Center.X, column.VerticalScrollbarThumbRectangle.Top - 1);

        Hold(controller, aboveTheMovedThumb, frames: 600);

        Assert.AreEqual(offsetAfterPress, column.ScrollOffset.Y);
    }

    [TestMethod]
    public void ColumnScrollbarInsideTheOuterWindowsResizeBand_WinsOverResize()
    {
        var windowService = CreateWindowService();
        var outer = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(300, 200), MaximumSize = new Vector2(600, 500), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserResize = true },
        });
        outer.Initialize();
        var column = CreateColumn(windowService, outer, Vector2.Zero, outer.ContentSize);
        var controller = CreateController(outer);
        var bar = column.VerticalScrollbarTrackRectangle;
        var pointInResizeBand = new Point(outer.Rectangle.Right - 7, bar.Center.Y);
        Assert.IsTrue(bar.Contains(pointInResizeBand), "Test setup: the point must be on the column's bar.");
        Assert.AreNotEqual(ResizeEdges.None, outer.GetResizeEdgesAt(pointInResizeBand), "Test setup: the point must be in the outer resize band.");

        Press(controller, pointInResizeBand);

        Assert.AreSame(column, controller.ActiveInteraction.Element);
        Assert.AreNotEqual(ScrollbarPart.None, controller.ActiveInteraction.ScrollbarPart);
    }

    [TestMethod]
    public void ColumnScrollbarInTheOuterWindowsResizeCorner_LosesToResize()
    {
        var windowService = CreateWindowService();
        var outer = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(300, 200), MaximumSize = new Vector2(600, 500), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserResize = true },
        });
        outer.Initialize();
        var column = CreateColumn(windowService, outer, Vector2.Zero, outer.ContentSize);
        var controller = CreateController(outer);
        var pointInCorner = new Point(outer.Rectangle.Right - 7, outer.Rectangle.Bottom - 7);
        Assert.IsTrue(column.VerticalScrollbarTrackRectangle.Contains(pointInCorner), "Test setup: the point must be on the column's bar.");

        Press(controller, pointInCorner);

        Assert.AreEqual(ElementDragInteractionKind.Resize, controller.ActiveInteraction.Kind);
    }

    [TestMethod]
    public void PressingAScrollbar_KeepsFocusWhereItWas_AndMakesTheWindowActive()
    {
        var windowService = CreateWindowService();
        var focusable = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 100), Size = new Vector2(100, 100), DisplayMode = ElementDisplayMode.Fixed },
        });
        focusable.Initialize();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(focusable, column);
        Press(controller, focusable.ContentRectangle.Center);
        Release(controller, focusable.ContentRectangle.Center);
        Assert.AreSame(focusable, controller.FocusedElement);

        Press(controller, column.VerticalScrollbarThumbRectangle.Center);

        Assert.AreSame(focusable, controller.FocusedElement);
        Assert.IsTrue(column.IsActiveWindow);
    }

    [TestMethod]
    public void HoveringTheThumb_SetsTheHoveredPart_AndMovingAwayClearsIt()
    {
        var windowService = CreateWindowService();
        var column = CreateRootColumn(windowService);
        var controller = CreateController(column);

        Move(controller, new Point(0, 0));
        Move(controller, column.VerticalScrollbarThumbRectangle.Center);
        Assert.AreEqual(ScrollbarPart.VerticalThumb, column.ScrollbarHoveredPart);

        Move(controller, column.ContentRectangle.Center);
        Assert.AreEqual(ScrollbarPart.None, column.ScrollbarHoveredPart);
    }

    private static readonly KeyboardState ShiftHeld = new(Keys.LeftShift);

    private static Window CreateTwoAxisScrollable(ElementPoolService windowService)
    {
        var window = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(200, 100), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserScrollHorizontal = true, CanUserScrollVertical = true, CanUserFocus = false },
        });
        window.Initialize();
        window.AddChild(windowService.CreateElement<Window>(window, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = new Vector2(600, 600), MaximumSize = new Vector2(600, 600), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { CanUserFocus = false },
        }));
        return window;
    }

    private static void WheelOneNotchDown(UiInputController controller, Point position, KeyboardState keyboardState)
    {
        controller.Update(keyboardState, new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
        controller.Update(keyboardState, new MouseState(position.X, position.Y, -120, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
    }

    [TestMethod]
    public void TwoAxisScrollable_ShowsBothBars()
    {
        var window = CreateTwoAxisScrollable(CreateWindowService());

        Assert.IsTrue(window.ShowVerticalScrollbar);
        Assert.IsTrue(window.ShowHorizontalScrollbar);
    }

    [TestMethod]
    public void Wheel_OverATwoAxisScrollable_ScrollsVertically()
    {
        var window = CreateTwoAxisScrollable(CreateWindowService());
        var controller = CreateController(window);

        WheelOneNotchDown(controller, window.ContentRectangle.Center, NoKeys);

        Assert.AreEqual(0f, window.ScrollOffset.X);
        Assert.IsGreaterThan(0f, window.ScrollOffset.Y);
    }

    [TestMethod]
    public void ShiftWheel_OverATwoAxisScrollable_ScrollsHorizontally()
    {
        var window = CreateTwoAxisScrollable(CreateWindowService());
        var controller = CreateController(window);

        WheelOneNotchDown(controller, window.ContentRectangle.Center, ShiftHeld);

        Assert.IsGreaterThan(0f, window.ScrollOffset.X);
        Assert.AreEqual(0f, window.ScrollOffset.Y);
    }

    [TestMethod]
    public void Wheel_OverTheHorizontalScrollbar_ScrollsHorizontally()
    {
        var window = CreateTwoAxisScrollable(CreateWindowService());
        var controller = CreateController(window);

        WheelOneNotchDown(controller, window.HorizontalScrollbarTrackRectangle.Center, NoKeys);

        Assert.IsGreaterThan(0f, window.ScrollOffset.X);
        Assert.AreEqual(0f, window.ScrollOffset.Y);
    }

    [TestMethod]
    public void ShiftWheel_OverAVerticalOnlyScrollable_StillScrollsVertically()
    {
        var column = CreateRootColumn(CreateWindowService());
        var controller = CreateController(column);

        WheelOneNotchDown(controller, column.ContentRectangle.Center, ShiftHeld);

        Assert.IsGreaterThan(0f, column.ScrollOffset.Y);
    }

    [TestMethod]
    public void HoveringATextBoxScrollbar_ShowsTheArrowCursor_NotTheIBeam()
    {
        var windowService = CreateWindowService();
        var textBox = windowService.CreateElement<TextBox>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(100, 100), Size = new Vector2(200, 60), MaximumSize = new Vector2(200, 60), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, CanUserScrollVertical = true },
            Text = new TextOptions { Multiline = true, Text = string.Join(" ", Enumerable.Repeat("word", 200)) },
        });
        textBox.Initialize();
        var controller = CreateController(textBox);
        Assert.IsTrue(textBox.ShowVerticalScrollbar, "Test setup: the text must overflow.");

        Move(controller, new Point(0, 0));
        Move(controller, textBox.ContentRectangle.Center);
        Assert.AreEqual(MouseCursor.IBeam, controller.CurrentCursor);

        Move(controller, textBox.VerticalScrollbarThumbRectangle.Center);
        Assert.AreEqual(MouseCursor.Arrow, controller.CurrentCursor);
    }
}
