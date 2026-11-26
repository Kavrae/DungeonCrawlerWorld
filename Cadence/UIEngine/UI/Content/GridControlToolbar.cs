using Microsoft.Xna.Framework;

namespace UIEngine.UI.Content;

/// <summary>One GridControl that every tab of a TabbedContent shares as its toolbar: one sort, one set of toggles and one search for whichever tab is shown.</summary>
/// <remarks>
/// TabbedContent never rebuilds its toolbar, so the search text, the sort and the toggles keep their state, and the
/// search box keeps focus, while the player switches tabs. A tab reads the current state as it opens (SortOptionIndex,
/// AppliedSearchText, IsToggleOn) and listens for changes while it is shown; nothing here knows what a tab or grid is.
/// </remarks>
public sealed class GridControlToolbar(
    ElementPoolService elementPoolService,
    IReadOnlyList<string> sortOptionLabels,
    IReadOnlyList<(string Label, bool DefaultOn)> toggles,
    string searchGhostText) : IElementContent
{
    private readonly bool[] _toggleStates = [.. toggles.Select(static toggle => toggle.DefaultOn)];

    private GridControl? _gridControl;
    private Window _hostWindow = null!;

    /// <summary>The sort option shown changed, to this index into the labels given.</summary>
    public event Action<int>? SortOptionCycled;

    /// <summary>The applied search text changed.</summary>
    public event Action<string>? SearchFilterChanged;

    /// <summary>A toggle, by its index into the toggles given, was switched to this state.</summary>
    public event Action<int, bool>? ToggleChanged;

    /// <summary>The index of the sort option shown; 0 until the control is built.</summary>
    public int SortOptionIndex => _gridControl?.SortOptionIndex ?? 0;

    /// <summary>The search text as last applied; empty for none.</summary>
    public string AppliedSearchText => _gridControl?.AppliedSearchText ?? string.Empty;

    /// <summary>Whether the toggle at index is on.</summary>
    public bool IsToggleOn(int index) => _toggleStates[index];

    /// <summary>Shows the toggle at index as isOn, without raising ToggleChanged -- for a setting something else can change too (a view another window switches).</summary>
    public void SetToggleOn(int index, bool isOn)
    {
        _toggleStates[index] = isOn;
        _gridControl?.SetToggleOn(index, isOn);
    }

    /// <summary>The count the control shows -- the shown tab's.</summary>
    public void SetItemCount(int count) => _gridControl?.SetItemCount(count);

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _gridControl = elementPoolService.CreateElement<GridControl>(hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(hostWindow.ContentSize.X, GridControl.RowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = ColorPalettes.WindowPalette.PanelBackgroundColor },
        });

        var toggleDefinitions = new List<(string Label, bool DefaultOn, Action<bool> OnToggled)>(toggles.Count);
        for (var index = 0; index < toggles.Count; index++)
        {
            var toggleIndex = index;
            toggleDefinitions.Add((toggles[index].Label, _toggleStates[index], isOn => OnToggled(toggleIndex, isOn)));
        }

        _gridControl.Configure(sortOptionLabels, toggleDefinitions, searchGhostText);
        hostWindow.AddChild(_gridControl);
        _gridControl.SortOptionCycled += index => SortOptionCycled?.Invoke(index);
        _gridControl.SearchFilterChanged += text => SearchFilterChanged?.Invoke(text);
        hostWindow.Resized += OnHostWindowResized;
    }

    public void Update(GameTime gameTime)
    {
    }

    public void DrawContent(GameTime gameTime)
    {
    }

    public void Deactivate()
    {
        elementPoolService.CloseAllChildren(_hostWindow);
        _gridControl = null;
    }

    private void OnToggled(int index, bool isOn)
    {
        _toggleStates[index] = isOn;
        ToggleChanged?.Invoke(index, isOn);
    }

    private void OnHostWindowResized(Element _) => _gridControl?.SetSize(new Vector2(_hostWindow.ContentSize.X, GridControl.RowHeight));
}
