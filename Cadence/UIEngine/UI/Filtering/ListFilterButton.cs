using System.Globalization;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI.Filtering;

/// <summary>
/// A button that opens a list's filter menu under itself in the shared Dropdown: each group's heading, a checkbox row per
/// choice -- an unchecked one with the count of items checking it would leave, a checked one with none, so the counts
/// read at a glance as what each choice would do -- and Clear filters last. It reads its label and,
/// while any are checked, how many.
/// </summary>
/// <remarks>
/// <para>
/// Opened by a click, or Space, Enter or Down while focused, as MultiChoiceField is; a press on it while the menu is open
/// closes it. Choosing a choice flips it and leaves the menu open; the rows are read again after every change to the
/// selection (and whenever the host calls RefreshMenu, after its items changed), keeping the menu's place, scroll and
/// highlight. A choice that would leave nothing is disabled unless checked, so a checked one can always be unchecked.
/// </para>
/// <para>
/// Configure before Initialize, the usual contract; the host places it, sizes it and may set its font (ContentFont) to
/// match the row it sits in. Closing it closes its menu and lets go of the selection.
/// </para>
/// </remarks>
public sealed class ListFilterButton(ElementServices services, DropdownController dropdowns)
    : Element(services)
{
    /// <summary>The button's label: a funnel-like wedge (U+26DB, from the fallback symbol font) -- Unicode has no funnel.</summary>
    public const string DefaultLabel = "⛛";
    public const string ClearFiltersLabel = "Clear filters";

    private IListFilterMenuSource? _source;
    private string _drawnLabel = DefaultLabel;

    public SpriteFontBase ContentFont { get; set; } = services.FontService.GetFont(FontChrome.DefaultFontSize);

    /// <summary>What the button reads before its count.</summary>
    public string Label { get; private set; } = DefaultLabel;

    /// <summary>What the button reads now: its label, then the count checked while any are.</summary>
    internal string DrawnLabel => _drawnLabel;

    public bool IsMenuOpen => dropdowns.IsOpen && ReferenceEquals(dropdowns.Owner, this);

    public override bool IsTabStop => Enabled;

    /// <summary>The widest the button reads with label and a count of checkedCount, for a host sizing it.</summary>
    public float MeasureWidth(int checkedCount) => ContentFont.MeasureString(LabelFor(Label, checkedCount)).X;

    /// <summary>Reads as a Button does: its background the options' content colour, else WindowPalette.PanelContentColor, under body text.</summary>
    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);
        SetContentColor(options.Content?.ContentColor ?? WindowPalette.PanelContentColor);
    }

    public void Configure(IListFilterMenuSource source, string label = DefaultLabel)
    {
        Release();
        _source = source;
        Label = label;
        source.Selection.Changed += OnSelectionChanged;
        _drawnLabel = LabelFor(label, source.Selection.CheckedCount);
    }

    /// <summary>Reads the open menu's rows again -- after the host's items changed; nothing while it is closed.</summary>
    public void RefreshMenu()
    {
        if (IsMenuOpen && _source is { } source)
        {
            dropdowns.ReplaceOptions(this, BuildOptions(source));
        }
    }

    protected override bool OnContentClickAction(Point mousePosition)
    {
        if (IsMenuOpen)
        {
            dropdowns.Close();
            return true;
        }

        OpenMenu();
        return true;
    }

    protected override bool OnKeyPressAction(Keys key)
    {
        if (key is Keys.Space or Keys.Enter or Keys.Down && !IsMenuOpen)
        {
            OpenMenu();
            return true;
        }

        return base.OnKeyPressAction(key);
    }

    protected internal override void OnClosed()
    {
        if (IsMenuOpen)
        {
            dropdowns.Close();
        }

        Release();
        Label = DefaultLabel;
        _drawnLabel = DefaultLabel;
        ContentFont = FontService.GetFont(FontChrome.DefaultFontSize);
        base.OnClosed();
    }

    private void Release()
    {
        if (_source is { } source)
        {
            source.Selection.Changed -= OnSelectionChanged;
            _source = null;
        }
    }

    private void OnSelectionChanged(ListFilterSelection selection)
    {
        _drawnLabel = LabelFor(Label, selection.CheckedCount);
        RefreshMenu();
    }

    private void OpenMenu()
    {
        if (!Enabled || _source is not { } source)
        {
            return;
        }

        dropdowns.OpenBelow(this, BuildOptions(source), -1, DropdownField.ListMaximumHeight);
    }

    private static List<DropdownOption> BuildOptions(IListFilterMenuSource source)
    {
        var selection = source.Selection;
        var options = new List<DropdownOption>();
        foreach (var row in source.MenuRows())
        {
            if (row.IsHeading)
            {
                options.Add(DropdownOption.Header(row.Label));
                continue;
            }

            var choiceIndex = row.ChoiceIndex;
            var isChecked = selection.IsChecked(choiceIndex);
            var countText = isChecked ? null : row.CountIfFlipped.ToString(CultureInfo.InvariantCulture);
            options.Add(new DropdownOption(row.Label, countText, Enabled: isChecked || row.CountIfFlipped > 0, () => selection.Flip(choiceIndex)) { IsChecked = isChecked, Icon = row.Icon, TooltipText = row.TooltipText });
        }

        options.Add(new DropdownOption(ClearFiltersLabel, null, Enabled: !selection.IsEmpty, selection.Clear));
        return options;
    }

    /// <summary>What a button labelled label reads with checkedCount choices checked: the label alone, else the label and the count ("⛛ 2").</summary>
    public static string LabelFor(string label, int checkedCount) => checkedCount == 0 ? label : $"{label} {checkedCount}";

    /// <summary>Room either side of the button's widest label.</summary>
    private const float LabelPadding = 2f;

    /// <summary>How wide a host makes the button in font for a filter of choiceCount choices: its widest label (every choice checked) and a little room either side.</summary>
    public static float WidthFor(SpriteFontBase font, int choiceCount) => font.MeasureString(LabelFor(DefaultLabel, choiceCount)).X + LabelPadding * 2;

    /// <summary>Draws the focus mark over everything else while focused.</summary>
    public override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);

        if (IsFocused)
        {
            DrawFocusIndicator(Rectangle);
        }
    }

    public override void DrawContent(GameTime gameTime)
    {
        var textWidth = ContentFont.MeasureString(_drawnLabel).X;
        var left = System.Math.Max(0f, (ContentSize.X - textWidth) / 2f);
        LabelRenderer.DrawLeftAligned(Render.Batch, ContentFont, _drawnLabel, new Vector2(left, 0), new Vector2(ContentSize.X - left, ContentSize.Y), Enabled ? WindowPalette.BodyTextColor : Color.Gray);
    }
}
