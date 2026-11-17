using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI.Fields;

/// <summary>
/// One editable value on one row: its label, indented to its depth, the control its kind takes, and a revert button shown
/// while the value differs from what it is compared with. A value the player changes is committed whole -- on Enter or
/// leaving a text, a wheel step or a submitted number, a pick -- never per keystroke.
/// </summary>
/// <remarks>
/// The row knows nothing of where its value lives: the owner reads Committed and writes the value back. It is a dark row
/// with a white label and a light control. A label is dimmed while the value is only the default an unset field means, and
/// coloured with its problem (SetProblem); its tooltip holds the problem, the hint and what an unset value means. The
/// revert request is raised on the update after the press, never inside it, since the owner answering rebuilds rows.
/// Configure before Initialize, the usual contract.
/// </remarks>
public sealed class FieldRow(ElementServices services)
    : Element(services)
{
    public const float RowHeight = 26f;

    /// <summary>A multi-line text row's height: about four lines.</summary>
    public const float MultilineRowHeight = 84f;
    public const float LabelColumnWidth = 200f;
    private const float Gap = 4f;

    /// <summary>What a number field needs beside its text: its border, its text's padding and room for the caret.</summary>
    private const float NumberTextAllowance = 20f;
    private const float NumberMinimumWidth = 48f;
    private const string RevertLabel = "<";

    /// <summary>What a dropdown or picker shows while nothing is chosen -- a required value not given yet.</summary>
    public const string NoSelectionPlaceholder = "Select...";

    /// <summary>A checkbox's fill while on -- the rows' own, and any editor checkbox beside them.</summary>
    public static readonly Color ToggleOnColor = new(80, 68, 30);

    private readonly SpriteFontBase _font = services.FontService.GetFont(FontChrome.DefaultFontSize);
    private readonly FontStashTextMeasurer _labelMeasurer = services.FontService.GetTextMeasurer(services.FontService.GetFont(FontChrome.DefaultFontSize));

    /// <summary>The label as drawn: cut with "..." to the label column, so it never runs under the control.</summary>
    private string _drawnLabel = string.Empty;

    private FieldEditorDescription _description = new(string.Empty, FieldValueKind.Text);
    private FieldValue _value;
    private string _committedText = string.Empty;
    private Color _labelColor = WindowPalette.ControlLabelTextColor;
    private Element _editor = null!;
    private Button _revertButton = null!;
    private bool _revertRequested;
    private bool _isChangeMarked;
    private string? _changeNote;

    public FieldEditorDescription Description => _description;

    /// <summary>The control that edits the value -- what a caller focuses to take the player to this field.</summary>
    public Element Editor => _editor;

    /// <summary>Raised with the new value when the player commits one.</summary>
    public event Action<FieldValue>? Committed;

    /// <summary>Raised when the player presses the revert button, shown while SetChangeMarker offers one.</summary>
    public event Action? RevertRequested;

    /// <summary>Whether the row is marked as differing from what it is compared with.</summary>
    public bool IsChangeMarked => _isChangeMarked;

    /// <summary>The revert button: hidden unless SetChangeMarker offers one.</summary>
    public Button RevertButton => _revertButton;

    /// <summary>The height a row of description's kind takes.</summary>
    public static float HeightOf(FieldEditorDescription description) => description.Kind == FieldValueKind.MultilineText ? MultilineRowHeight : RowHeight;

    private bool IsTextKind => _description.Kind is FieldValueKind.Text or FieldValueKind.MultilineText or FieldValueKind.ReadOnlyText;

    public void Configure(FieldEditorDescription description, FieldValue value)
    {
        _description = description;
        _value = value;
        _committedText = IsTextKind ? value.Text : string.Empty;
        _revertRequested = false;
        _isChangeMarked = false;
        _changeNote = null;
        _problem = null;
        FitLabel();
        _labelColor = LabelColorFor(problem: null, isWarning: false);
    }

    /// <summary>Cuts the label to the room it has, with "..." only when it doesn't fit, and puts the whole label in the tooltip when it is cut.</summary>
    private void FitLabel()
    {
        _drawnLabel = StringUtility.TruncateWithEllipsis(_labelMeasurer, _description.Label, LabelWidth);
        TooltipText = ComposeTooltip();
    }

    private string? _problem;

    /// <summary>Colours the label for a problem with the value (null for none), and puts it in the label's tooltip.</summary>
    public void SetProblem(string? problem, bool isWarning)
    {
        _problem = problem;
        _labelColor = LabelColorFor(problem, isWarning);
        TooltipText = ComposeTooltip();
    }

    /// <summary>Marks the row as differing (a bar at its left edge) with note in its tooltip, and shows the revert button with revertTooltip -- none for null; unmarks it for isMarked false.</summary>
    public void SetChangeMarker(bool isMarked, string? note, string? revertTooltip)
    {
        _isChangeMarked = isMarked;
        _changeNote = isMarked ? note : null;
        TooltipText = ComposeTooltip();
        var showsRevert = isMarked && revertTooltip is not null;
        _revertButton.TooltipText = revertTooltip;
        if (_revertButton.IsVisible != showsRevert)
        {
            _revertButton.IsVisible = showsRevert;
        }
    }

    /// <summary>The label as drawn: the whole label, or its start with "..." when the label column can't hold it.</summary>
    public string DrawnLabel => _drawnLabel;

    /// <summary>Where the label starts: the inset and one indent per level.</summary>
    private float LabelLeft => FieldSectionHeader.TextInset + _description.Depth * FieldSectionHeader.IndentWidth;

    /// <summary>How wide the label may be drawn: the label column after its indent, less the gap before the control -- or, for a label-only row, the row after its indent, less room for its buttons.</summary>
    private float LabelWidth => System.Math.Max(1f, (_description.IsLabelOnly ? ContentSize.X - ButtonsWidth : LabelColumnWidth) - LabelLeft - Gap);

    /// <summary>The revert button's place, which is kept whether it is shown or not.</summary>
    private const float ButtonsWidth = FieldSectionHeader.ButtonWidth + Gap;

    /// <summary>The whole label when it is drawn cut, then the problem, what it is compared with, the hint and what an unset value means -- whichever there are.</summary>
    private string? ComposeTooltip()
    {
        string?[] parts = [_drawnLabel != _description.Label ? _description.Label : null, _problem, _changeNote, _description.Hint, _description.Tooltip];
        var present = parts.Where(part => !string.IsNullOrEmpty(part)).ToArray();
        return present.Length == 0 ? null : string.Join("\n", present);
    }

    /// <summary>Shows the value as set from now on -- the owner wrote the first value of a field that was only its default.</summary>
    public void MarkSet()
    {
        if (_description.IsSet)
        {
            return;
        }

        _description = _description with { IsSet = true, Tooltip = null };
        _problem = null;
        TooltipText = ComposeTooltip();
        _labelColor = LabelColorFor(problem: null, isWarning: false);
    }

    /// <summary>Puts the committed value back over anything typed and not yet committed; false when nothing was.</summary>
    public bool RevertUncommitted() => _editor switch
    {
        SearchableChoiceField searchable => searchable.RevertTypedText(),
        NumericField numeric when numeric.OriginalText != FormatNumber(numeric.Value) => RevertNumber(numeric),
        ColorField color when color.OriginalText != ColorField.Format(color.Value) => RevertColor(color),
        TextBox textBox when _description.Kind is FieldValueKind.Text or FieldValueKind.MultilineText && textBox.OriginalText != _committedText => RevertText(textBox),
        _ => false,
    };

    /// <summary>Commits text typed into a text, number, colour or searchable field and not yet committed, as Enter would; false when nothing was.</summary>
    public bool CommitUncommitted()
    {
        switch (_editor)
        {
            case SearchableChoiceField searchable:
                return searchable.CommitTypedText();
            case NumericField numeric when numeric.OriginalText != FormatNumber(numeric.Value):
                numeric.CommitTypedText();
                return true;
            case ColorField color when color.OriginalText != ColorField.Format(color.Value):
                color.CommitTypedText();
                return true;
            case TextBox textBox when _description.Kind is FieldValueKind.Text or FieldValueKind.MultilineText && textBox.OriginalText != _committedText:
                CommitText(textBox);
                return true;
            default:
                return false;
        }
    }

    private bool RevertNumber(NumericField numeric)
    {
        numeric.SetValue(numeric.Value);
        return true;
    }

    private static bool RevertColor(ColorField color)
    {
        color.SetValue(color.Value);
        return true;
    }

    private bool RevertText(TextBox textBox)
    {
        textBox.SetText(_committedText);
        return true;
    }

    /// <summary>The decimal places the number field shows: none for a whole number, whatever the description says.</summary>
    private int NumberDecimalPlaces => _description.Kind == FieldValueKind.Integer ? 0 : _description.DecimalPlaces;

    private string FormatNumber(double value) => value.ToString("F" + NumberDecimalPlaces.ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);

    private Color LabelColorFor(string? problem, bool isWarning) =>
        problem is not null ? isWarning ? Color.Goldenrod : Color.IndianRed
        : _description.IsSet ? WindowPalette.ControlLabelTextColor : WindowPalette.DimmedLabelTextColor;

    public override void Initialize()
    {
        base.Initialize();

        ContentPadding = Vector2.Zero;
        _editor = CreateEditor();
        AddChild(_editor);
        if (IsTextKind && _editor is TextBox textBox)
        {
            textBox.SetText(_value.Text);
        }

        _revertButton = ElementPoolService.CreateElement<Button>(this, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(FieldSectionHeader.ButtonWidth, FieldSectionHeader.ButtonHeight), DisplayMode = ElementDisplayMode.Fixed },
        });
        _revertButton.SetText(RevertLabel);
        _revertButton.Clicked += _ => _revertRequested = true;
        AddChild(_revertButton);
        _revertButton.IsVisible = false;
        if (_description.IsLabelOnly)
        {
            _editor.IsVisible = false;
        }

        ContentResized += _ => LayOut();
        LayOut();
    }

    /// <summary>
    /// The label column, then the control, then the revert button's place, which is kept whether it is shown or not so
    /// the control never moves. A label-only row has no control: its label takes the row, and the revert button stands
    /// right after the label as drawn.
    /// </summary>
    private void LayOut()
    {
        FitLabel();
        var buttonTop = (RowHeight - FieldSectionHeader.ButtonHeight) / 2f;
        var buttonSize = new Vector2(FieldSectionHeader.ButtonWidth, FieldSectionHeader.ButtonHeight);
        if (_description.IsLabelOnly)
        {
            var labelRight = LabelLeft + _font.MeasureString(_drawnLabel).X;
            _revertButton.SetBounds(new Vector2(labelRight + Gap, buttonTop), buttonSize);
            return;
        }

        var editorX = LabelColumnWidth;
        var availableWidth = System.Math.Max(1f, ContentSize.X - editorX - ButtonsWidth);
        var editorWidth = System.Math.Min(availableWidth, PreferredEditorWidth() ?? availableWidth);
        var editorSize = new Vector2(editorWidth, ContentSize.Y);
        _editor.SetMaximumSize(editorSize);
        _editor.SetBounds(new Vector2(editorX, 0), editorSize);
        _revertButton.SetBounds(new Vector2(editorX + editorWidth + Gap, buttonTop), buttonSize);
    }

    /// <summary>
    /// The width the control needs, for a control narrower than the row: a checkbox its own square, a number the widest
    /// value its limits allow (the minimum's sign included); null for one that takes the row's width.
    /// </summary>
    private float? PreferredEditorWidth()
    {
        switch (_description.Kind)
        {
            case FieldValueKind.Boolean:
                return Toggle.CheckboxSize;
            case FieldValueKind.Integer or FieldValueKind.Number:
                var widest = System.Math.Max(_font.MeasureString(FormatNumber(_description.Minimum)).X, _font.MeasureString(FormatNumber(_description.Maximum)).X);
                return System.Math.Max(NumberMinimumWidth, widest + NumberTextAllowance);
            default:
                return null;
        }
    }

    private Element CreateEditor() => _description.Kind switch
    {
        FieldValueKind.ReadOnlyText => CreateValueLabel(),
        FieldValueKind.Text or FieldValueKind.MultilineText => CreateTextBox(),
        FieldValueKind.Integer or FieldValueKind.Number => CreateNumericField(),
        FieldValueKind.Boolean => CreateToggle(),
        FieldValueKind.Choice => CreateDropdownField(),
        FieldValueKind.SearchableChoice => CreateSearchableChoiceField(),
        FieldValueKind.MultiChoice => CreateMultiChoiceField(),
        _ => CreateColorField(),
    };

    private ElementOptions EditorOptions(bool showBorder = true, bool isMultiline = false, bool isTransparent = false) => new()
    {
        Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
        Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(1, HeightOf(_description)), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = isTransparent },
        Chrome = new ElementChromeOptions { ShowBorder = showBorder, ShowTitle = false, CanUserScrollVertical = isMultiline },
        Content = new ElementContentOptions { ContentColor = WindowPalette.EditableControlBackground },
        Text = new TextOptions { TextColor = WindowPalette.BodyTextColor, Multiline = isMultiline },
    };

    /// <summary>A value that can't be changed, shown as a label on the row rather than in a control -- nothing to click, focus or tab to.</summary>
    private TextWindow CreateValueLabel() => ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
    {
        Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
        Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(1, RowHeight), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
        Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        Text = new TextOptions { Text = _value.Text, TextColor = WindowPalette.ControlLabelTextColor },
    });

    private TextBox CreateTextBox()
    {
        var textBox = ElementPoolService.CreateElement<TextBox>(this, EditorOptions(isMultiline: _description.Kind == FieldValueKind.MultilineText));
        textBox.GhostTextColor = WindowPalette.GhostTextOnLightColor;
        textBox.TextSubmitted += _ => CommitText(textBox);
        textBox.FocusChanged += _ =>
        {
            if (!textBox.IsFocused)
            {
                CommitText(textBox);
            }
        };

        return textBox;
    }

    private void CommitText(TextBox textBox)
    {
        if (textBox.OriginalText == _committedText)
        {
            return;
        }

        _committedText = textBox.OriginalText;
        Committed?.Invoke(FieldValue.OfText(_committedText));
    }

    private NumericField CreateNumericField()
    {
        var field = ElementPoolService.CreateElement<NumericField>(this, EditorOptions());
        field.Configure(_value.Number, _description.Minimum, _description.Maximum, _description.Step, NumberDecimalPlaces);
        field.ValueChanged += value => Committed?.Invoke(FieldValue.OfNumber(value));
        return field;
    }

    private Toggle CreateToggle()
    {
        var toggle = ElementPoolService.CreateElement<Toggle>(this, EditorOptions(showBorder: false, isTransparent: true));
        toggle.Configure(_value.Boolean, ToggleOnColor, WindowPalette.EditableControlBackground, isOn => Committed?.Invoke(FieldValue.OfBoolean(isOn)));
        return toggle;
    }

    private DropdownField CreateDropdownField()
    {
        var field = ElementPoolService.CreateElement<DropdownField>(this, EditorOptions());
        field.SetOptions(_description.Choices, _value.ChoiceIndex);
        field.Placeholder = NoSelectionPlaceholder;
        field.SelectionChanged += index => Committed?.Invoke(FieldValue.OfChoice(index, _description.Choices[index]));
        return field;
    }

    private SearchableChoiceField CreateSearchableChoiceField()
    {
        var field = ElementPoolService.CreateElement<SearchableChoiceField>(this, EditorOptions());
        field.Configure(_description.Choices, _value.ChoiceIndex, _description.AllowsFreeText, _value.Text);
        field.GhostText = NoSelectionPlaceholder;
        field.GhostTextColor = WindowPalette.GhostTextOnLightColor;
        field.Chosen += (index, text) => Committed?.Invoke(FieldValue.OfChoice(index, text));
        return field;
    }

    private MultiChoiceField CreateMultiChoiceField()
    {
        var field = ElementPoolService.CreateElement<MultiChoiceField>(this, EditorOptions());
        field.Configure(_description.Choices, _value.ChoiceIndices ?? []);
        field.Placeholder = "None";
        field.ChoicesChanged += indices => Committed?.Invoke(FieldValue.OfChoices(indices));
        return field;
    }

    private ColorField CreateColorField()
    {
        var field = ElementPoolService.CreateElement<ColorField>(this, EditorOptions());
        field.Configure(_value.Color);
        field.ValueChanged += color => Committed?.Invoke(FieldValue.OfColor(color));
        return field;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_revertRequested)
        {
            _revertRequested = false;
            RevertRequested?.Invoke();
        }
    }

    public override void DrawContent(GameTime gameTime)
    {
        if (_isChangeMarked)
        {
            Render.Batch.FillRectangle(new Rectangle(0, 0, WindowPalette.ChangedValueMarkerWidth, (int)ContentSize.Y), WindowPalette.ChangedValueMarkerColor);
        }

        LabelRenderer.DrawLeftAligned(Render.Batch, _font, _drawnLabel, new Vector2(LabelLeft, 0), new Vector2(LabelWidth, System.Math.Min(ContentSize.Y, RowHeight)), _labelColor);
    }
}
