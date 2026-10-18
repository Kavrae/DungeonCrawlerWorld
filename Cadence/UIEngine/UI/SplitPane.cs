using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>Whether a SplitPane's two panes sit side by side (a vertical splitter) or one above the other (a horizontal one).</summary>
public enum SplitOrientation : byte
{
    SideBySide,
    Stacked,
}

/// <summary>Which of a SplitPane's panes keeps its size when the split pane itself is resized; the other takes the change.</summary>
public enum SplitSizedPane : byte
{
    First,
    Second,
}

/// <summary>
/// Two panes with a splitter between them that the pointer drags -- a tab's columns, the IDE's bottom panel. One pane
/// keeps its size when the split pane is resized and the other takes the change; dragging the splitter changes the
/// sized pane, never past either pane's minimum. The owner fills FirstPane and SecondPane like any window.
/// </summary>
/// <remarks>Configure before Initialize, the usual contract.</remarks>
public sealed class SplitPane(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer)
    : Element(fontService, elementPoolService, labelRenderer)
{
    public const float SplitterThickness = 5f;

    private static readonly Color SplitterColor = WindowPalette.ControlBackground;

    private SplitOrientation _orientation;
    private SplitSizedPane _sizedPane;
    private float _sizedPaneSize;
    private float _firstPaneMinimumSize;
    private float _secondPaneMinimumSize;
    private float? _dragStartSizedPaneSize;

    public Window FirstPane { get; private set; } = null!;

    public Window SecondPane { get; private set; } = null!;

    public SplitOrientation Orientation => _orientation;

    /// <summary>The sized pane's width (side by side) or height (stacked).</summary>
    public float SizedPaneSize => _sizedPaneSize;

    /// <summary>Raised with the sized pane's new size when the splitter is dragged.</summary>
    public event Action<float>? SplitChanged;

    public void Configure(SplitOrientation orientation, SplitSizedPane sizedPane, float sizedPaneSize, float firstPaneMinimumSize = 40f, float secondPaneMinimumSize = 40f)
    {
        _orientation = orientation;
        _sizedPane = sizedPane;
        _sizedPaneSize = sizedPaneSize;
        _firstPaneMinimumSize = firstPaneMinimumSize;
        _secondPaneMinimumSize = secondPaneMinimumSize;
        _dragStartSizedPaneSize = null;
    }

    public override void Initialize()
    {
        base.Initialize();

        ContentPadding = Vector2.Zero;
        FirstPane = CreatePane();
        SecondPane = CreatePane();
        ContentResized += _ => LayOutPanes();
        LayOutPanes();
    }

    private Window CreatePane()
    {
        var pane = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = Vector2.One, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        pane.ContentPadding = Vector2.Zero;
        AddChild(pane);
        return pane;
    }

    /// <summary>Sets the sized pane's size, held within both minimums.</summary>
    public void SetSizedPaneSize(float size)
    {
        _sizedPaneSize = size;
        LayOutPanes();
    }

    private float Extent => _orientation == SplitOrientation.SideBySide ? ContentSize.X : ContentSize.Y;

    private float FirstPaneExtent => _sizedPane == SplitSizedPane.First ? _sizedPaneSize : Extent - SplitterThickness - _sizedPaneSize;

    private void LayOutPanes()
    {
        var available = System.Math.Max(0f, Extent - SplitterThickness);
        var sizedMinimum = _sizedPane == SplitSizedPane.First ? _firstPaneMinimumSize : _secondPaneMinimumSize;
        var otherMinimum = _sizedPane == SplitSizedPane.First ? _secondPaneMinimumSize : _firstPaneMinimumSize;
        _sizedPaneSize = System.Math.Max(0f, System.Math.Min(System.Math.Max(_sizedPaneSize, sizedMinimum), available - otherMinimum));

        var firstExtent = System.Math.Max(0f, FirstPaneExtent);
        var secondExtent = System.Math.Max(0f, available - firstExtent);
        if (_orientation == SplitOrientation.SideBySide)
        {
            FirstPane.SetBounds(Vector2.Zero, new Vector2(firstExtent, ContentSize.Y));
            SecondPane.SetBounds(new Vector2(firstExtent + SplitterThickness, 0), new Vector2(secondExtent, ContentSize.Y));
            return;
        }

        FirstPane.SetBounds(Vector2.Zero, new Vector2(ContentSize.X, firstExtent));
        SecondPane.SetBounds(new Vector2(0, firstExtent + SplitterThickness), new Vector2(ContentSize.X, secondExtent));
    }

    private Rectangle SplitterRectangle
    {
        get
        {
            var content = ContentRectangle;
            var offset = (int)FirstPaneExtent;
            return _orientation == SplitOrientation.SideBySide
                ? new Rectangle(content.X + offset, content.Y, (int)SplitterThickness, content.Height)
                : new Rectangle(content.X, content.Y + offset, content.Width, (int)SplitterThickness);
        }
    }

    protected override bool IsPointerDragHandleAt(Point position) => SplitterRectangle.Contains(position);

    protected internal override MouseCursor PointerDragCursor => _orientation == SplitOrientation.SideBySide ? MouseCursor.SizeWE : MouseCursor.SizeNS;

    protected override void OnPointerDragAction(Vector2 totalMouseDelta)
    {
        _dragStartSizedPaneSize ??= _sizedPaneSize;

        var delta = _orientation == SplitOrientation.SideBySide ? totalMouseDelta.X : totalMouseDelta.Y;
        var previous = _sizedPaneSize;
        _sizedPaneSize = _dragStartSizedPaneSize.Value + (_sizedPane == SplitSizedPane.First ? delta : -delta);
        LayOutPanes();
        if (_sizedPaneSize != previous)
        {
            SplitChanged?.Invoke(_sizedPaneSize);
        }
    }

    protected override void OnPointerDragEndedAction() => _dragStartSizedPaneSize = null;

    public override void DrawContent(GameTime gameTime) =>
        ElementPoolService.SpriteBatch.Draw(ElementPoolService.UnitRectangle, SplitterRectangle, SplitterColor);
}
