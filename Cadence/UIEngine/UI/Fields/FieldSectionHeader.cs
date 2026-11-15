using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI.Fields;

/// <summary>
/// The heading row of a group of field rows -- an object, a list, one element of a list -- indented to its depth, with an
/// open/closed marker when it can be collapsed and small buttons at its right (Add, Remove, move up and down).
/// </summary>
/// <remarks>
/// A press on the heading opens or closes it; a button asks. Both are raised on the update after the press, never inside
/// it, since the owner answering rebuilds the rows -- this one included. Configure before Initialize, the usual contract.
/// </remarks>
public sealed class FieldSectionHeader(ElementServices services)
    : Element(services)
{
    public const float IndentWidth = 14f;

    /// <summary>How far a heading's title and a row's label sit in from the row's left edge -- a window title's own inset (Window.TitlePadding).</summary>
    public const float TextInset = 5f;
    public const float ButtonWidth = 18f;
    private const float WideButtonWidth = 42f;

    /// <summary>A heading's buttons are this tall, centred in the row -- about the text's own height.</summary>
    public const float ButtonHeight = 18f;
    private const float ButtonGap = 2f;
    private const float MarkerWidth = 12f;
    private const string ExpandedMarker = "-";
    private const string CollapsedMarker = "+";
    private const string RevertLabel = "<";

    private readonly SpriteFontBase _font = services.FontService.GetFont(FontChrome.DefaultFontSize);
    private readonly List<Button> _buttons = [];

    private string _title = string.Empty;
    private string? _hint;
    private bool _buttonsFollowTitle;

    /// <summary>A gap between the title and buttons that follow it.</summary>
    private const float TitleButtonsGap = 8f;
    private int _depth;
    private bool _isCollapsible;
    private IReadOnlyList<string> _buttonLabels = [];
    private IReadOnlyList<bool> _buttonsEnabled = [];
    private float _buttonsWidth;
    private Color _titleColor = WindowPalette.ControlLabelTextColor;
    private Action? _pendingRequest;
    private Button _revertButton = null!;
    private string? _problem;
    private bool _isChangeMarked;
    private string? _changeNote;
    private string _changeCountSuffix = string.Empty;

    public bool IsExpanded { get; private set; }

    /// <summary>Whether the heading is marked as differing, itself or in what it holds.</summary>
    public bool IsChangeMarked => _isChangeMarked;

    /// <summary>The title as drawn: the title, and while closed how many changes it hides.</summary>
    public string DrawnTitle => _title + _changeCountSuffix;

    /// <summary>The revert button at the row's right edge: hidden unless SetChangeMarker offers one.</summary>
    public Button RevertButton => _revertButton;

    /// <summary>Raised when the player presses the revert button.</summary>
    public event Action? RevertRequested;

    public string Title => _title;

    /// <summary>The buttons at the right, in the order Configure was given their labels.</summary>
    public IReadOnlyList<Button> Buttons => _buttons;

    /// <summary>Raised with the new state when the heading is opened or closed.</summary>
    public event Action<bool>? ExpandToggled;

    /// <summary>Raised with the index of the button pressed, into the labels Configure was given.</summary>
    public event Action<int>? ButtonClicked;

    /// <param name="buttonLabels">The buttons at the right, left to right; a one-character label makes a small button.</param>
    /// <param name="buttonsEnabled">Whether each button can be pressed; null for all of them.</param>
    /// <param name="hint">What explains the group beyond its title, shown in its tooltip; null for none.</param>
    /// <param name="buttonsFollowTitle">The buttons sit just right of the title (a list element's move and remove, beside what they act on) rather than at the row's right edge.</param>
    public void Configure(string title, int depth, bool isCollapsible, bool isExpanded, IReadOnlyList<string> buttonLabels, IReadOnlyList<bool>? buttonsEnabled = null, string? hint = null, bool buttonsFollowTitle = false)
    {
        _title = title;
        _buttonsFollowTitle = buttonsFollowTitle;
        _hint = hint;
        TooltipText = hint;
        _depth = depth;
        _isCollapsible = isCollapsible;
        IsExpanded = isExpanded;
        _buttonLabels = buttonLabels;
        _buttonsWidth = buttonLabels.Sum(label => WidthOf(label) + ButtonGap);
        _buttonsEnabled = buttonsEnabled ?? [.. buttonLabels.Select(_ => true)];
        _titleColor = WindowPalette.ControlLabelTextColor;
        _pendingRequest = null;
        _problem = null;
        _isChangeMarked = false;
        _changeNote = null;
        _changeCountSuffix = string.Empty;
        _buttons.Clear();
    }

    /// <summary>Colours the title for a problem in what the heading holds (null for none), and puts it in the tooltip.</summary>
    public void SetProblem(string? problem, bool isWarning)
    {
        _problem = problem;
        _titleColor = problem is null ? WindowPalette.ControlLabelTextColor : isWarning ? Color.Goldenrod : Color.IndianRed;
        TooltipText = ComposeTooltip();
    }

    /// <summary>
    /// Marks the heading as differing (a bar at its left edge) with note in its tooltip; while closed, its title counts
    /// hiddenChangeCount changes inside; the revert button is shown with revertTooltip, none for null. isMarked false
    /// unmarks it.
    /// </summary>
    public void SetChangeMarker(bool isMarked, string? note, int hiddenChangeCount, string? revertTooltip)
    {
        _isChangeMarked = isMarked;
        _changeNote = isMarked ? note : null;
        _changeCountSuffix = isMarked && !IsExpanded && hiddenChangeCount > 0 ? $" - {hiddenChangeCount} changed" : string.Empty;
        TooltipText = ComposeTooltip();
        var showsRevert = isMarked && revertTooltip is not null;
        _revertButton.TooltipText = revertTooltip;
        if (_revertButton.IsVisible != showsRevert)
        {
            _revertButton.IsVisible = showsRevert;
        }
    }

    private string? ComposeTooltip()
    {
        string?[] parts = [_problem, _changeNote, _hint];
        var present = parts.Where(part => !string.IsNullOrEmpty(part)).ToArray();
        return present.Length == 0 ? null : string.Join("\n", present);
    }

    public override void Initialize()
    {
        base.Initialize();

        ContentPadding = Vector2.Zero;
        for (var index = 0; index < _buttonLabels.Count; index++)
        {
            var capturedIndex = index;
            var button = ElementPoolService.CreateElement<Button>(this, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(WidthOf(_buttonLabels[index]), 1), DisplayMode = ElementDisplayMode.Fixed },
                Text = new TextOptions { Text = _buttonLabels[index] },
            });
            button.Enabled = _buttonsEnabled[index];
            button.Clicked += _ => _pendingRequest = () => ButtonClicked?.Invoke(capturedIndex);
            AddChild(button);
            _buttons.Add(button);
        }

        _revertButton = ElementPoolService.CreateElement<Button>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(ButtonWidth, ButtonHeight), DisplayMode = ElementDisplayMode.Fixed },
            Text = new TextOptions { Text = RevertLabel },
        });
        _revertButton.Clicked += _ => _pendingRequest = () => RevertRequested?.Invoke();
        AddChild(_revertButton);
        _revertButton.IsVisible = false;

        ContentResized += _ => LayOutButtons();
        LayOutButtons();
    }

    private static float WidthOf(string label) => label.Length <= 1 ? ButtonWidth : WideButtonWidth;

    /// <summary>The revert button's place at the right edge, kept whether it is shown or not; the other buttons follow the title or stand left of it.</summary>
    private void LayOutButtons()
    {
        _revertButton.SetBounds(new Vector2(ContentSize.X - ButtonWidth, ButtonTop), new Vector2(ButtonWidth, ButtonHeight));
        if (_buttonsFollowTitle)
        {
            var left = System.Math.Min(TitleLeft + _font.MeasureString(_title).X + TitleButtonsGap, System.Math.Max(TitleLeft, ContentSize.X - ButtonsWidth));
            foreach (var (button, label) in _buttons.Zip(_buttonLabels))
            {
                var width = WidthOf(label);
                button.SetBounds(new Vector2(left, ButtonTop), new Vector2(width, ButtonHeight));
                left += width + ButtonGap;
            }

            return;
        }

        var x = ContentSize.X - RevertSlotWidth;
        for (var index = _buttons.Count - 1; index >= 0; index--)
        {
            var width = WidthOf(_buttonLabels[index]);
            x -= width;
            _buttons[index].SetBounds(new Vector2(x, ButtonTop), new Vector2(width, ButtonHeight));
            x -= ButtonGap;
        }
    }

    /// <summary>Where the title starts: the inset, the indent, and the open/closed marker when there is one.</summary>
    private float TitleLeft => TextInset + _depth * IndentWidth + (_isCollapsible ? MarkerWidth : 0f);

    private float ButtonTop => System.Math.Max(0f, (ContentSize.Y - ButtonHeight) / 2f);

    private const float RevertSlotWidth = ButtonWidth + ButtonGap;

    private float ButtonsWidth => _buttonsWidth + RevertSlotWidth;

    protected override bool OnContentClickAction(Point mousePosition)
    {
        if (!_isCollapsible)
        {
            return false;
        }

        _pendingRequest = () =>
        {
            IsExpanded = !IsExpanded;
            ExpandToggled?.Invoke(IsExpanded);
        };
        return true;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_pendingRequest is { } request)
        {
            _pendingRequest = null;
            request();
        }
    }

    public override void DrawContent(GameTime gameTime)
    {
        if (_isChangeMarked)
        {
            Render.Batch.FillRectangle(new Rectangle(0, 0, WindowPalette.ChangedValueMarkerWidth, (int)ContentSize.Y), WindowPalette.ChangedValueMarkerColor);
        }

        var x = TextInset + _depth * IndentWidth;
        if (_isCollapsible)
        {
            LabelRenderer.DrawLeftAligned(Render.Batch, _font, IsExpanded ? ExpandedMarker : CollapsedMarker, new Vector2(x, 0), new Vector2(MarkerWidth, ContentSize.Y), _titleColor);
            x += MarkerWidth;
        }

        var titleRight = _buttonsFollowTitle && _buttons.Count > 0 ? _buttons[0].RelativePosition.X - ButtonGap : ContentSize.X - ButtonsWidth;
        LabelRenderer.DrawLeftAligned(Render.Batch, _font, DrawnTitle, new Vector2(x, 0), new Vector2(System.Math.Max(1f, titleRight - x), ContentSize.Y), _titleColor);
    }
}
